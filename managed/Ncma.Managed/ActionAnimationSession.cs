using System.Text.Json;
using V3 = System.Numerics.Vector3;
using Q = System.Numerics.Quaternion;
namespace Ncma;

/// <summary>Consumer-owned isolated preview policy/history. Native ABI 2 is numerical only.</summary>
public sealed class ActionAnimationSession : IDisposable
{
    private readonly int _owner=Environment.CurrentManagedThreadId;
    private readonly AnimationKernel _kernel;
    private readonly JsonElement[] _clips;
    private readonly List<State> _undo=[], _redo=[];
    private bool _disposed;
    public const int HistoryLimit=128;
    public ulong Revision { get; private set; }
    private sealed record Event(Guid Clip,string Name,double Offset);
    private readonly record struct Motion(V3 Position,Q Rotation,V3 Scale) {
        public static Motion Identity => new(V3.Zero,Q.Identity,V3.One);
        public static Motion Read(float[] data) => new(new(data[0],data[1],data[2]),new(data[3],data[4],data[5],data[6]),new(data[7],data[8],data[9]));
        public Motion Combine(Motion local) => new(Position+V3.Transform(Scale*local.Position,Rotation),Q.Normalize(Rotation*local.Rotation),Scale*local.Scale);
        public object Json() => new { position=new[]{Position.X,Position.Y,Position.Z},rotation_xyzw=new[]{Rotation.X,Rotation.Y,Rotation.Z,Rotation.W},scale=new[]{Scale.X,Scale.Y,Scale.Z} };
    }
    private sealed class State {
        public int Clip; public double Time,Speed,BlendTime,BlendDuration; public bool Paused=true,Action,Hit,Combo,Invulnerable;
        public float[] Pose=[], From=[]; public Event[] Events=[]; public Motion Actor=Motion.Identity,Root=Motion.Identity;
        public State Copy() => (State)MemberwiseClone(); // arrays are immutable after publication
    }
    private State _state;
    public ActionAnimationSession(string? libraryPath=null) {
        _kernel=new(libraryPath??Path.Combine(AppContext.BaseDirectory,"NcmaNative.dll"));
        _clips=_kernel.Metadata.GetProperty("clips").EnumerateArray().Select(c=>c.Clone()).ToArray();
        _state=Initial();
    }
    private State Initial() => new() {Pose=_kernel.Sample(0,0)};
    private void Verify() {
        if (Environment.CurrentManagedThreadId!=_owner) throw new InvalidOperationException("Animation owner thread required.");
        ObjectDisposedException.ThrowIf(_disposed,this);
    }
    public double Time { get {Verify();return _state.Time;} }
    public bool Paused { get {Verify();return _state.Paused;} }
    private string Name(State s) => _clips[s.Clip].GetProperty("name").GetString()!;
    private double Duration(State s) => _clips[s.Clip].GetProperty("duration").GetDouble();
    private bool Loop(State s) => _clips[s.Clip].GetProperty("loop").GetBoolean();
    private static float Weight(State s) => s.BlendDuration==0?1:(float)Math.Min(1,s.BlendTime/s.BlendDuration);
    private void Evaluate(State s) => s.Pose=_kernel.Sample(s.Clip,s.Time,s.BlendDuration==0?null:s.From,Weight(s));
    private void Play(State s,string name,double blend) {
        int clip=Array.FindIndex(_clips,c=>c.GetProperty("name").GetString()==name);
        if (clip<0) throw new ArgumentException("Unknown clip.");
        s.From=s.Pose[..(_kernel.Bones*10)]; s.Clip=clip; s.Time=0; s.BlendTime=0; s.BlendDuration=blend;
        s.Root=Motion.Identity; s.Events=[]; Evaluate(s);
    }
    private void Advance(State s,double seconds) {
        if (!double.IsFinite(seconds) || seconds is <0 or >1) throw new ArgumentException("Step seconds must be finite within 0..1.");
        var events=new List<Event>(); Motion root=Motion.Identity; double elapsed=0;
        while (seconds>0) {
            double rate=s.Action || s.Speed==0?1:.5+s.Speed*.5;
            double segment=s.Action?Math.Min(seconds,Math.Max(0,Duration(s)-s.Time)/rate):seconds;
            double next=Loop(s)?s.Time+segment*rate:Math.Min(s.Time+segment*rate,Duration(s));
            Motion delta=Motion.Read(_kernel.Motion(s.Clip,s.Time,next)); s.Actor=s.Actor.Combine(delta); root=root.Combine(delta);
            foreach(var notify in _kernel.Notifies(s.Clip,s.Time,next)) {
                string name=notify.GetProperty("name").GetString()!;
                switch(name) {
                    case "Hit.Start": s.Hit=true;break; case "Hit.End":s.Hit=false;break;
                    case "Combo.Open":s.Combo=true;break;case "Combo.Close":s.Combo=false;break;
                    case "Invulnerability.Start":s.Invulnerable=true;break;case "Invulnerability.End":s.Invulnerable=false;break;
                }
                events.Add(new(notify.GetProperty("clip_uuid").GetGuid(),name,elapsed+notify.GetProperty("offset").GetDouble()/rate));
            }
            s.Time=Loop(s)?next%Duration(s):next;
            s.BlendTime=Math.Min(s.BlendTime+segment*rate,s.BlendDuration); Evaluate(s);
            seconds=Math.Max(0,seconds-segment); elapsed+=segment;
            if (s.Action && s.Time>=Duration(s)) {
                s.Action=false; s.Hit=s.Combo=s.Invulnerable=false; Play(s,s.Speed>.1?"Run":"Idle",.15);
            } else break;
        }
        s.Root=root; s.Events=events.ToArray();
    }
    public void Execute(uint command,double value=0,string text="",ulong? expectedRevision=null) {
        Verify(); if (expectedRevision.HasValue && expectedRevision.Value!=Revision) throw new InvalidOperationException("stale_preview");
        if (!double.IsFinite(value)) throw new ArgumentException("Command value must be finite.");
        ulong revision=checked(Revision+1);
        if (command is 6 or 7) {
            var source=command==6?_undo:_redo; var target=command==6?_redo:_undo;
            if (source.Count==0) throw new InvalidOperationException("Animation history is empty.");
            target.Add(_state); _state=source[^1]; source.RemoveAt(source.Count-1);
        } else {
            State candidate=_state.Copy();
            switch(command) {
                case 1:
                    if(value is <0 or >1)throw new ArgumentException("Speed must be within 0..1.");
                    candidate.Speed=value;
                    if (!candidate.Action && Name(candidate)!=(value>.1?"Run":"Idle")) Play(candidate,value>.1?"Run":"Idle",.15);
                    break;
                case 2:
                    if (text is not ("Attack" or "Dodge")) throw new ArgumentException("Action must be Attack or Dodge.");
                    if (candidate.Action && !(text=="Attack" && candidate.Combo)) throw new InvalidOperationException("Action locked; Attack chains only in combo window.");
                    Play(candidate,text,text=="Attack"?.08:.05); candidate.Action=true; candidate.Hit=candidate.Combo=candidate.Invulnerable=false; break;
                case 3:
                    if (value is not (0 or 1))throw new ArgumentException("Paused must be 0 or 1.");
                    candidate.Paused=value==1; break;
                case 4: Advance(candidate,value); break;
                case 5: candidate=Initial(); break;
                default: throw new ArgumentException("Unknown animation command.");
            }
            if(command is 1 or 2 or 3) {candidate.Events=[];candidate.Root=Motion.Identity;}
            _undo.Add(_state); if(_undo.Count>HistoryLimit)_undo.RemoveAt(0);_redo.Clear();_state=candidate;
        }
        Revision=revision;
    }
    public void SetSpeed(double speed)=>Execute(1,speed);
    public void TriggerAction(string action)=>Execute(2,0,action);
    public void SetPaused(bool paused)=>Execute(3,paused?1:0);
    public void Step(double seconds)=>Execute(4,seconds);
    public void Reset()=>Execute(5);
    public void Undo()=>Execute(6);
    public void Redo()=>Execute(7);
    public void Tick(double seconds) {
        Verify(); if (!double.IsFinite(seconds) || seconds is <0 or >1)throw new ArgumentException("Invalid preview delta.");
        if (!_state.Paused) {var candidate=_state.Copy();Advance(candidate,seconds);_state=candidate;}
    }
    public string InspectJson() {
        Verify(); State s=_state; var metadata=_kernel.Metadata;
        object Local(int bone) => Motion.Read(s.Pose[(bone*10)..(bone*10+10)]).Json();
        var bones=metadata.GetProperty("bones").EnumerateArray().Select((bone,i)=>new {
            name=bone.GetProperty("name").GetString(),parent=bone.GetProperty("parent").GetInt32(),
            position=new[]{s.Pose[_kernel.Bones*10+i*16+12],s.Pose[_kernel.Bones*10+i*16+13],s.Pose[_kernel.Bones*10+i*16+14]}, local=Local(i)
        }).ToArray();
        return JsonSerializer.Serialize(new {schema_version=1,session_kind="action_animation_preview",revision=Revision,
            state=Name(s),time=s.Time,duration=Duration(s),blend_weight=Weight(s),paused=s.Paused,speed=s.Speed,
            action_active=s.Action,hit_window=s.Hit,combo_window=s.Combo,invulnerable=s.Invulnerable,
            can_undo=_undo.Count!=0,can_redo=_redo.Count!=0,actor=s.Actor.Json(),root_delta=s.Root.Json(),
            events=s.Events.Select(e=>new {clip_uuid=e.Clip,name=e.Name,offset=e.Offset}),skeleton_uuid=metadata.GetProperty("skeleton_uuid").GetGuid(),
            bones,clips=_clips});
    }
    public void Dispose() {
        if (_disposed)return;Verify();_kernel.Dispose();_undo.Clear();_redo.Clear();_disposed=true;
    }
}
