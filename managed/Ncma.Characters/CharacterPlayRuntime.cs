using V3 = System.Numerics.Vector3;
using Q = System.Numerics.Quaternion;
using M4 = System.Numerics.Matrix4x4;
using Ncma.Gameplay;
using Ncma.Movement;
using Ncma.Physics;
using Ncma.Runtime;
using Ncma.Scene.Rendering;
using Ncma.Animation;
namespace Ncma.Characters;

// Application-owned policy; same composition in Editor/Player/headless. Native receives
// numbers only. This is not an Agent input injection interface or a general gameplay SDK.
public sealed partial class CharacterPlayRuntime : IDisposable, IWorldSystem, ICommittedStepObserver, IRootMotionPresentation
{
    private readonly PlaySession _play;
    private readonly MovementCoordinator _coordinator;
    private readonly Binding[] _bindings;
    private readonly RootMotionSet _roots;
    private readonly ActionController _actions;
    private Domain? _domain;
    private bool _disposed;
    private sealed record Binding(Guid Id, CharacterData? Character, BoxColliderData? Box);
    public CoupledMovementStatus Status => _coordinator.Status;
    public Guid NumericalEpoch { get { _play.Document.VerifyAccess(); return _domain?.Epoch ?? Guid.Empty; } }
    public static CharacterPlayRuntime? Compose(PlaySession play, PhysicsService physics, PreparedSceneAssetLease? assets = null, bool profile = false)
    {
        ArgumentNullException.ThrowIfNull(play); ArgumentNullException.ThrowIfNull(physics);
        var startup = play.Document.CaptureSnapshot();
        CharacterComponents.RequireComposition(startup);
        if (!CharacterComponents.HasPhysics(startup)) return null;
        if (!physics.Enabled) throw new ArgumentException("Scene physics bindings require explicit physicsEnabled=true.");
        return new(play, physics, assets,profile);
    }
    private CharacterPlayRuntime(PlaySession play, PhysicsService physics, PreparedSceneAssetLease? assets,bool profile)
    {
        _play = play;_profileEnabled=profile;
        _roots=new(play.Document.World,assets);
        try{_actions=new(play.Document.World,_roots);}catch{_roots.Dispose();throw;}
        _bindings = play.Document.World.GetObjects().Where(o => o.Has<CharacterData>() || o.Has<BoxColliderData>()).OrderBy(o => o.PersistentId)
            .Select(o => new Binding(o.PersistentId, o.Has<CharacterData>() ? o.Get<CharacterData>() : null, o.Has<BoxColliderData>() ? o.Get<BoxColliderData>() : null)).ToArray();
        _coordinator = new(play, _bindings.Select(b => b.Id).ToArray(), () => {
            _profileTick=0;_roots.Initialize(play.Document.World);_actions.Initialize(play.Document.World,play.SessionId);return _domain = new Domain(physics, _bindings);
        },StageCombat);
        Guid[] chars = _bindings.Where(b => b.Character is not null).Select(b => b.Id).ToArray();
        Guid[] boxes = _bindings.Where(b => b.Box is not null).Select(b => b.Id).ToArray();
        Guid[] cameras = play.Document.World.GetObjects().Where(o => o.Has<FollowCameraData>()).Select(o => o.PersistentId).ToArray();
        // Freeze complete membership, including an empty set: runtime attachment must not
        // silently create metadata with no corresponding numerical/presentation owner.
        _coordinator.FreezeConfiguration<CharacterData>(chars);
        _coordinator.FreezeConfiguration<BoxColliderData>(boxes);
        _coordinator.FreezeConfiguration<FollowCameraData>(cameras);
        _coordinator.FreezeConfiguration<RootMotionData>(_roots.Ids);
        _coordinator.FreezeConfiguration<ActionDefinitionData>(_actions.ActorIds);
        if(_actions.HealthIds.Length!=0)_coordinator.RegisterPublication<HealthData>(_actions.HealthIds,_actions.Health);
        if(_roots.Ids.Length!=0) {
            _coordinator.FreezeConfiguration<SkinnedMeshData>(_roots.Ids,freezeMembership:false);
            _coordinator.FreezeConfiguration<ClipPlaybackData>(_roots.Ids,freezeMembership:false);
        }
        play.AddSystem(this);
        play.AddCommittedObserver(this);
    }
    public void FixedUpdate(World world, double delta)
    {
        if (_disposed || world != _play.Document.World || _domain is null || !Status.SnapshotValid) throw new InvalidOperationException("Stale character domain.");
        var input = _play.Input; float h = (float)delta;
        _actionMs=_rootMs=_queryMs=_combatMs=0;long preparation=ProfileTime,actionTime=ProfileTime;
        _actions.BeginStep();_actionMs+=ProfileElapsed(actionTime);
        for (int i = 0; i < _bindings.Length; ++i)
        {
            var binding = _bindings[i]; if (binding.Character is not { } data) continue;
            var state = _domain.Character(i);
            V3 direction = data.Controlled && input.Focused ? new((input.Held(68) ? 1 : 0) - (input.Held(65) ? 1 : 0), 0, (input.Held(83) ? 1 : 0) - (input.Held(87) ? 1 : 0)) : V3.Zero;
            if(!_actions.Alive(binding.Id))direction=V3.Zero;
            if (direction.LengthSquared() > 1) direction = V3.Normalize(direction);
            float vertical = state.Velocity.Y;
            if (state.Ground == GroundState.Ground && vertical <= state.GroundVelocity.Y) vertical = state.GroundVelocity.Y - 1;
            vertical = MathF.Max(-100, vertical + data.Gravity * h);
            if (_actions.Alive(binding.Id) && data.Controlled && input.Focused && input.Pressed(32) && state.Ground == GroundState.Ground) vertical = data.JumpSpeed;
            V3 velocity = direction * data.Speed + new V3(0, vertical, 0);
            if (state.Ground == GroundState.Ground) velocity += new V3(state.GroundVelocity.X, 0, state.GroundVelocity.Z);
            var start = world.FindObject(binding.Id).Get<TransformData>(); float turn = 0;
            if(_roots.Contains(binding.Id)) {
                actionTime=ProfileTime;
                bool rootMode=!_actions.Owns(binding.Id)||_actions.Prepare(binding.Id,input,data.Controlled,direction.LengthSquared()>1e-6f,delta);
                _actionMs+=ProfileElapsed(actionTime);long rootTime=ProfileTime;
                var root=_roots.Prepare(binding.Id,start,delta);_rootMs+=ProfileElapsed(rootTime);
                // Root motion REPLACES horizontal input and input turn, not an additive second writer.
                if(rootMode){velocity=new V3(root.Translation.X/h,vertical,root.Translation.Z/h);
                if(state.Ground==GroundState.Ground)velocity+=new V3(state.GroundVelocity.X,0,state.GroundVelocity.Z);
                _coordinator.Submit(new(_coordinator.CurrentStep,binding.Id,velocity*h,root.Yaw));continue;}
            }
            if (direction.LengthSquared() > 1e-6f)
            {
                float current = 2 * MathF.Atan2(start.Rotation.Y, start.Rotation.W), desired = MathF.Atan2(-direction.X, -direction.Z);
                float difference = MathF.IEEERemainder(desired - current, 2 * MathF.PI);
                turn = Math.Clamp(difference, -Math.Min(MathF.PI, data.TurnSpeed * h), Math.Min(MathF.PI, data.TurnSpeed * h));
            }
            _coordinator.Submit(new(_coordinator.CurrentStep, binding.Id, velocity * h, turn));
        }
        _preparationMs=ProfileElapsed(preparation);
    }
    public void StepCommitted(World world,double h){long start=ProfileTime;_roots.Commit(world,h);_actions.Commit(world);_commitMs=ProfileElapsed(start);_profileTick=world.Tick;}
    private void StageCombat(MovementStepStamp stamp,ReadOnlySpan<NumericMovementResult> results)
    {
        if(stamp.SessionId!=_play.SessionId||stamp.WorldId!=_play.Document.World.Identity||stamp.FromTick!=_play.Tick)throw new InvalidOperationException("Combat stamp mismatch.");
        long start=ProfileTime;
        _actions.StageHits((actor,reach,mask)=>{long query=ProfileTime;Guid result=_domain!.QueryTarget(actor,reach,mask);_queryMs+=ProfileElapsed(query);return result;});
        _combatMs=ProfileElapsed(start);
    }
    public void RequestAction(Guid objectId,ActionRequest request){_coordinator.VerifyControlBoundary();_actions.Request(objectId,request);}
    public ActionStatus InspectAction(Guid objectId){VerifyRootPresentation();return _actions.Inspect(objectId);}
    public CombatEvent[] ReadCombatEvents(){VerifyRootPresentation();return _actions.Events();}
    public CharacterDebugFrame ReadDebugFrame()
    {
        _play.Document.VerifyAccess();ObjectDisposedException.ThrowIf(_disposed,this);
        _coordinator.VerifyReadBoundary();
        bool valid=_play.State is PlayState.Running or PlayState.Paused && Status.SnapshotValid;
        var rows=new List<CharacterDebugRow>(32);
        if(valid)foreach(var binding in _bindings.Where(b=>b.Character is not null)) {
            var obj=_play.Document.World.FindObject(binding.Id);int index=Array.IndexOf(_bindings,binding);
            var numeric=_domain!.Character(index);bool root=_roots.Contains(binding.Id);
            var motion=root?_roots.Inspect(binding.Id):default;
            var action=_actions.Owns(binding.Id)?_actions.Inspect(binding.Id):default;
            HealthData? hp=obj.Has<HealthData>()?obj.Get<HealthData>():null;
            rows.Add(new(binding.Id,obj.Get<TransformData>().Position,numeric.Ground.ToString(),_domain.ContactCount(index),root,motion.Time,motion.DesiredDisplacement,motion.AcceptedDisplacement,
                _actions.Owns(binding.Id)?action.State.ToString():"None",action.Instance,hp?.Current,hp?.Maximum));
        }
        return new(_play.SessionId,_play.Document.World.Identity,_play.Tick,_play.State.ToString(),valid,valid?"none":_play.State==PlayState.Faulted?"play_faulted":"snapshot_invalid",rows.ToArray(),valid?_actions.Events():[]);
    }
    // Trusted owner-thread control at a safe boundary. Candidate clocks are never consumed before commit.
    public void SetRootPlayback(Guid objectId,ClipPlaybackData settings)
    {
        _play.Document.VerifyAccess();
        // Status is synchronized only outside a candidate step; World write access rejects read-only callbacks.
        _coordinator.VerifyControlBoundary();
        if(_actions.Owns(objectId))throw new InvalidOperationException("Action policy owns this actor's playback selection.");
        if(_disposed||_play.State is not (PlayState.Running or PlayState.Paused)||!Status.SnapshotValid)
            throw new InvalidOperationException("Root playback controls require a synchronized safe boundary.");
        _roots.Request(objectId,settings);
    }
    private void VerifyRootPresentation() {
        _play.Document.VerifyAccess();_coordinator.VerifyReadBoundary();if(_disposed||_play.State==PlayState.Faulted||!Status.SnapshotValid)throw new InvalidOperationException("Invalid coupled root motion presentation.");
    }
    public ClipPlaybackData Playback(Guid objectId){VerifyRootPresentation();return _roots.Playback(objectId);}
    public ClipSampleTimes Sample(Guid objectId,float alpha,bool paused){VerifyRootPresentation();return _roots.Sample(objectId,alpha,paused);}
    public void RemoveRoot(Guid objectId,Span<M4> models){VerifyRootPresentation();_roots.RemoveRoot(objectId,models);}
    public RootMotionStatus InspectRootMotion(Guid objectId){VerifyRootPresentation();return _roots.Inspect(objectId);}
    // Pure presentation: interpolated committed target, never World/solver writes.
    public static SceneCameraView? FollowView(PlaySession play, Guid camera, uint width, uint height)
    {
        if (camera == Guid.Empty || play.State == PlayState.Faulted) return null;
        var obj = play.Document.World.GetObjects().FirstOrDefault(o => o.PersistentId == camera);
        if (obj is null || !obj.Has<FollowCameraData>() || !obj.Has<CameraData>()) return null;
        var data = obj.Get<FollowCameraData>(); var target = play.RenderView.Objects.FirstOrDefault(o => o.ObjectId == data.Target);
        if (target is null) return null;
        var position = target.Transform.Position + new V3(data.OffsetX, data.OffsetY, data.OffsetZ);
        var view = M4.CreateLookAt(position, target.Transform.Position + new V3(0, data.LookHeight, 0), V3.UnitY);
        if (!M4.Invert(view, out var cameraWorld) || !M4.Decompose(cameraWorld, out _, out var rotation, out _)) throw new ArgumentException("Invalid follow view.");
        return RenderSceneExtractor.CreateCamera(Guid.Empty, new(position, Q.Normalize(rotation), V3.One), obj.Get<CameraData>(), width, height);
    }
    public void Dispose()
    {
        if (_disposed) return;
        _coordinator.Dispose(); _play.RemoveCommittedObserver(this);_roots.Dispose(); _disposed = true;
    }

    private sealed class Domain(PhysicsService service, Binding[] bindings) : INumericMovementAdapter
    {
        private PhysicsWorld? _world;
        private readonly int[] _characters = bindings.Select((b, i) => (b, i)).Where(p => p.b.Character is not null).Select(p => p.i).ToArray();
        private readonly int[] _boxes = bindings.Select((b, i) => (b, i)).Where(p => p.b.Box is not null).Select(p => p.i).ToArray();
        private readonly ulong[] _characterIds = new ulong[bindings.Count(b => b.Character is not null)], _boxIds = new ulong[bindings.Count(b => b.Box is not null)];
        private readonly CharacterState[] _states = new CharacterState[bindings.Count(b => b.Character is not null)];
        private readonly BodyState3D[] _bodies = new BodyState3D[bindings.Count(b => b.Box is not null)];
        private readonly CharacterVelocity[] _velocities = new CharacterVelocity[bindings.Count(b => b.Character is not null)];
        private readonly CharacterContact[] _contacts = new CharacterContact[bindings.Count(b => b.Character is not null) * 64];
        private readonly TransformData[] _published = new TransformData[bindings.Length];
        private readonly int[] _contactCounts=new int[bindings.Length];
        private Guid _session, _identity; private ulong _sequence; private bool _ready, _charactersCreated;
        public Guid Epoch { get; } = Guid.NewGuid();
        public CharacterState Character(int binding) => _states[Array.IndexOf(_characters, binding)];
        public int ContactCount(int binding)=>_contactCounts[binding];
        public PhysicsCounters Counters=>_world!.NativeCounters;
        public Guid QueryTarget(Guid actor,float reach,uint mask)
        {
            int i=Array.FindIndex(bindings,b=>b.Id==actor),n=Array.IndexOf(_characters,i);
            if(n<0)throw new ArgumentException("Unknown attack capsule.");var state=_states[n];
            var hit=_world!.Ray(_sequence,new(){StructSize=40,Mask=mask,Origin=state.Foot+new V3(0,bindings[i].Character!.Value.Radius+bindings[i].Character!.Value.HalfHeight,0),
                Displacement=V3.Transform(new(0,0,-reach),state.Rotation),Ignore=_characterIds[n]});
            if(hit.Sequence!=_sequence)throw new ArgumentException("Stale combat query sequence.");
            if(hit.Hit==0)return Guid.Empty;
            int target=hit.Kind==CollisionResourceKind.Character ? Array.IndexOf(_characterIds,hit.Resource) : hit.Kind==CollisionResourceKind.Body ? Array.IndexOf(_boxIds,hit.Resource) : -1;
            if(target<0)throw new ArgumentException("Foreign combat query result.");return bindings[hit.Kind==CollisionResourceKind.Character?_characters[target]:_boxes[target]].Id;
        }
        public void Initialize(Guid session, Guid identity, ReadOnlySpan<NumericMovementInput> startup)
        {
            if (_world is not null || startup.Length != bindings.Length) throw new ArgumentException("Invalid character startup.");
            _session = session; _identity = identity; _world = service.CreateCharacterDomain(new(0, -9.81f, 0));
            var boxes = new CollisionBox[_boxes.Length];
            for (int n = 0; n < _boxes.Length; ++n)
            {
                int i = _boxes[n]; var b = bindings[i]; var d = b.Box!.Value;
                boxes[n] = CollisionBox.Create(startup[i].Start.Position, new(d.HalfX, d.HalfY, d.HalfZ), d.Category, d.Dynamic);
                boxes[n].Density = d.Density; boxes[n].Rotation = startup[i].Start.Rotation; boxes[n].Correlation = (ulong)i + 1;
            }
            _world.CreateCollisionBoxes(boxes, _boxIds);
            var characters = new CapsuleDescription[_characters.Length];
            for (int n = 0; n < _characters.Length; ++n)
            {
                int i = _characters[n]; var d = bindings[i].Character!.Value;
                var c = CapsuleDescription.Default(startup[i].Start.Position + new V3(0, d.FootOffsetY, 0), d.Category, d.Mask);
                c.Radius = d.Radius; c.HalfHeight = d.HalfHeight; c.Padding = Math.Min(.02f, d.Radius * .1f); c.MaximumSlopeRadians = d.MaximumSlope;
                c.StepHeight = d.StepHeight; c.FloorDistance = d.FloorDistance; c.Correlation = (ulong)i + 1; characters[n] = c;
            }
            if (characters.Length != 0) { _world.CreateCapsules(characters, _characterIds); _charactersCreated = true; _world.ReadCharacterStates(0, _characterIds, _states); }
            for (int i = 0; i < startup.Length; ++i) { if (startup[i].ObjectId != bindings[i].Id) throw new ArgumentException("Startup target identity mismatch."); _published[i] = startup[i].Start; }
            _ready = true;
        }
        public void Preflight(MovementStepStamp stamp, double h, ReadOnlySpan<NumericMovementInput> inputs)
        {
            if (!_ready || stamp.SessionId != _session || stamp.WorldId != _identity || stamp.Sequence != _sequence + 1 || inputs.Length != bindings.Length || !double.IsFinite(h) || h <= 0 || h > .25)
                throw new ArgumentException("Stale numerical movement domain.");
            for (int i = 0; i < inputs.Length; ++i)
            {
                if (inputs[i].ObjectId != bindings[i].Id || inputs[i].Start != _published[i] || bindings[i].Box is not null && (inputs[i].Displacement != V3.Zero || inputs[i].YawRadians != 0))
                    throw new ArgumentException("Bound transforms/box intentions disagree with numerical publication.");
            }
        }
        public NumericMovementReceipt Execute(MovementStepStamp stamp, double h, ReadOnlySpan<NumericMovementInput> inputs, Span<NumericMovementResult> output)
        {
            Preflight(stamp, h, inputs);
            if (output.Length != bindings.Length) throw new ArgumentException("Numerical output capacity mismatch.");
            for (int n = 0; n < _characters.Length; ++n)
            {
                int i = _characters[n]; _velocities[n] = new()
                {
                    Character = _characterIds[n],
                    Velocity = inputs[i].Displacement / (float)h,
                    Rotation = Q.Normalize(inputs[i].Start.Rotation * Q.CreateFromAxisAngle(V3.UnitY, inputs[i].YawRadians))
                };
            }
            Array.Clear(_contactCounts);ulong next;
            if(_characters.Length==0)next=_world!.Step((float)h);
            else {
                var receipt=_world!.StepCharacters(_sequence,(float)h,_velocities,_states,_contacts);next=receipt.Sequence;
                if(receipt.Contacts>_contacts.Length)throw new ArgumentException("Contact receipt budget.");
                for(int n=0;n<receipt.Contacts;n++) {
                    var contact=_contacts[n];int character=Array.IndexOf(_characterIds,contact.Character);
                    if(character<0||contact.Sequence!=next)throw new ArgumentException("Contact identity/sequence mismatch.");
                    _contactCounts[_characters[character]]++;
                }
            }
            if (next != stamp.Sequence) throw new ArgumentException("Numerical sequence mismatch.");
            if (_boxes.Length != 0) _world.ReadBodyStates3D(next, _boxIds, _bodies);
            for (int i = 0; i < inputs.Length; ++i) output[i] = new(bindings[i].Id, inputs[i].Start);
            for (int n = 0; n < _boxes.Length; ++n)
            {
                int i = _boxes[n]; var body = _bodies[n]; if (body.Body != _boxIds[n] || body.Sequence != next || body.Correlation != (ulong)i + 1) throw new ArgumentException("Body mapping mismatch.");
                output[i] = new(bindings[i].Id, inputs[i].Start with { Position = body.Position, Rotation = Q.Normalize(body.Rotation) });
            }
            for (int n = 0; n < _characters.Length; ++n)
            {
                int i = _characters[n]; var c = _states[n]; if (c.Character != _characterIds[n] || c.Sequence != next || c.Correlation != (ulong)i + 1) throw new ArgumentException("Character mapping mismatch.");
                output[i] = new(bindings[i].Id, inputs[i].Start with { Position = c.Foot - new V3(0, bindings[i].Character!.Value.FootOffsetY, 0), Rotation = Q.Normalize(c.Rotation) });
            }
            for (int i = 0; i < output.Length; ++i) _published[i] = output[i].Transform;
            _sequence = next; return new(stamp, output.Length);
        }
        public void Dispose()
        {
            _ready = false; if (_world is null) return;
            if (_charactersCreated) { _world.DestroyCharacterSet(_characterIds); _charactersCreated = false; }
            _world.Dispose(); _world = null;
        }
    }
}
