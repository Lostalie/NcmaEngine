using System.Security.Cryptography;
using System.Text;
using Ncma.Animation;
using Ncma.Gameplay;
using Ncma.Runtime;
namespace Ncma.Characters;

public enum CharacterAction { Idle, Run, Attack, Dodge }
public enum ActionRequest { Attack, Dodge }
public enum CombatEventKind { Enter, Exit, HitOpen, HitClose, InvulnerableOpen, InvulnerableClose, Hit, Blocked }
public readonly record struct CombatEvent(Guid SessionId,Guid WorldId,ulong Tick,Guid Actor,Guid Target,Guid NotifyId,ulong Instance,CombatEventKind Kind,float Damage);
public readonly record struct ActionStatus(Guid ObjectId,CharacterAction State,ulong Instance,bool ComboQueued,double Time,ulong Tick);
// Fixed-step policy only. Candidate private state/events/damage install after successful commit.
internal sealed class ActionController
{
    private sealed class State
    {
        internal CharacterAction Action;
        internal ulong Instance,AttackExpiry,DodgeExpiry;
        internal bool AttackBuffered,DodgeBuffered,Combo;
        internal HashSet<Guid> HitTargets=[];
        internal State Copy()=>new(){Action=Action,Instance=Instance,AttackExpiry=AttackExpiry,DodgeExpiry=DodgeExpiry,AttackBuffered=AttackBuffered,DodgeBuffered=DodgeBuffered,Combo=Combo,HitTargets=new(HitTargets)};
    }
    private sealed class Actor(Guid id,ActionDefinitionData definition)
    {
        internal readonly Guid Id=id;
        internal readonly ActionDefinitionData Definition=definition;
        internal readonly Dictionary<CombatEventKind,Guid> NotifyIds=Enum.GetValues<CombatEventKind>().ToDictionary(k=>k,k=>new Guid(SHA256.HashData(Encoding.UTF8.GetBytes(definition.DefinitionId.ToString("N")+":"+k)).AsSpan(0,16)));
        internal State Committed=new(),Candidate=new();
        internal ClipInterval Interval;
        internal double Duration;
        internal bool Invulnerable,HitActive;
        internal ActionRequest? Requested;
    }
    private readonly Dictionary<Guid,Actor> _actors=[];
    private readonly Dictionary<Guid,HealthData> _startupHealth=[];
    private readonly Dictionary<Guid,HealthData> _health=[];
    private readonly RootMotionSet _roots;
    private readonly List<CombatEvent> _events=new(256);
    private CombatEvent[] _committedEvents=[];
    private World? _world;
    private Guid _session;
    private ulong _tick;
    internal ActionController(World world,RootMotionSet roots)
    {
        _roots=roots;
        foreach(var obj in world.GetObjects().OrderBy(o=>o.PersistentId)) {
            if(obj.Has<HealthData>())_startupHealth.Add(obj.PersistentId,obj.Get<HealthData>());
            if(!obj.Has<ActionDefinitionData>())continue;
            var d=obj.Get<ActionDefinitionData>();_=ActionDefinitionData.Validate(d);
            if(!_roots.Contains(obj.PersistentId))throw new ArgumentException("Actions require a pinned root-motion character.");
            foreach(Guid clip in new[]{d.IdleClip,d.RunClip,d.AttackClip,d.DodgeClip})_=roots.Duration(obj.PersistentId,clip);
            _actors.Add(obj.PersistentId,new(obj.PersistentId,d));
        }
        if(_actors.Count>32)throw new ArgumentException("Action actor budget is 32 (at most 256 committed events per step).");
    }
    internal Guid[] ActorIds=>_actors.Keys.ToArray();
    internal Guid[] HealthIds=>_startupHealth.Keys.ToArray();
    internal bool Owns(Guid id)=>_actors.ContainsKey(id);
    internal HealthData Health(Guid id)=>_health[id];
    internal bool Alive(Guid id)=>!_health.TryGetValue(id,out var health)||health.Current>0;
    internal void Initialize(World world,Guid session)
    {
        _world=world;_session=session;_tick=world.Tick;_events.Clear();_committedEvents=[];_health.Clear();
        foreach(var pair in _startupHealth)_health.Add(pair.Key,pair.Value);
        foreach(var a in _actors.Values){a.Committed=new();a.Candidate=new();a.Requested=null;a.Invulnerable=a.HitActive=false;}
    }
    internal void Request(Guid id,ActionRequest request)
    {if(!_actors.TryGetValue(id,out var a)||!Enum.IsDefined(request))throw new ArgumentException("Unknown actor/request.");a.Requested=request;}
    private void Event(Actor a,CombatEventKind kind,Guid target=default,float damage=0)
    {
        if(_events.Count>=256)throw new InvalidOperationException("Combat event budget exceeded.");
        _events.Add(new(_session,_world!.Identity,_world.Tick+1,a.Id,target,a.NotifyIds[kind],a.Candidate.Instance,kind,damage));
    }
    private static bool Within(double phase,float begin,float end)=>phase>=begin&&phase<end;
    private static bool Overlap(double a,double b,float begin,float end)=>a<end&&b>begin;
    internal bool Prepare(Guid id,InputState input,bool controlled,bool moving,double h)
    {
        var a=_actors[id];var d=a.Definition;var next=a.Committed.Copy();a.Candidate=next;
        bool attack=a.Requested==ActionRequest.Attack || controlled&&input.Focused&&input.Pressed(74);
        bool dodge=a.Requested==ActionRequest.Dodge || controlled&&input.Focused&&input.Pressed(75);
        ulong tick=_world!.Tick;
        if(next.AttackBuffered&&tick>next.AttackExpiry)next.AttackBuffered=false;
        if(next.DodgeBuffered&&tick>next.DodgeExpiry)next.DodgeBuffered=false;
        if(attack){next.AttackBuffered=true;next.AttackExpiry=checked(tick+(ulong)d.BufferTicks);}
        if(dodge){next.DodgeBuffered=true;next.DodgeExpiry=checked(tick+(ulong)d.BufferTicks);}
        bool entered=next.Instance==0, alive=!_health.TryGetValue(id,out var hp)||hp.Current>0;
        if(!alive){next.AttackBuffered=next.DodgeBuffered=next.Combo=false;moving=false;}
        double phase=next.Instance==0 ? 0 : _roots.Time(id)/Duration(a,next.Action);
        CharacterAction desired=next.Action;
        bool restart=entered;
        if(next.Action==CharacterAction.Attack && next.AttackBuffered && Within(phase,d.ComboStart,d.ComboEnd)) {next.Combo=true;next.AttackBuffered=false;}
        if(next.Action is CharacterAction.Idle or CharacterAction.Run || phase>=1 || !alive) {
            if(alive&&next.DodgeBuffered){desired=CharacterAction.Dodge;next.DodgeBuffered=false;}
            else if(alive&&(next.AttackBuffered||next.Action==CharacterAction.Attack&&next.Combo)){desired=CharacterAction.Attack;next.AttackBuffered=false;}
            else desired=moving?CharacterAction.Run:CharacterAction.Idle;
            restart=entered||desired!=next.Action || phase>=1&&desired is CharacterAction.Attack or CharacterAction.Dodge;
        } else if(next.Action==CharacterAction.Attack&&next.DodgeBuffered&&Within(phase,d.CancelStart,d.CancelEnd)) {desired=CharacterAction.Dodge;next.DodgeBuffered=false;restart=true;}
        if(restart) {
            if(!entered){
                // Exiting a still-open window closes it once, even on cancellation/death.
                if(next.Action==CharacterAction.Attack&&Within(phase,d.HitStart,d.HitEnd))Event(a,CombatEventKind.HitClose);
                if(next.Action==CharacterAction.Dodge&&Within(phase,d.InvulnerableStart,d.InvulnerableEnd))Event(a,CombatEventKind.InvulnerableClose);
                Event(a,CombatEventKind.Exit);
            }
            next.Action=desired;next.Instance=checked(next.Instance+1);next.Combo=false;next.HitTargets.Clear();Event(a,CombatEventKind.Enter);
        }
        Guid clip=Clip(a,next.Action);var playback=new ClipPlaybackData(clip,true,next.Action is CharacterAction.Idle or CharacterAction.Run,1,0);
        a.Duration=Duration(a,next.Action);a.Interval=_roots.Interval(id,playback,h,restart);
        _roots.SelectForStep(id,playback,restart);
        double from=a.Interval.Previous/a.Duration,to=a.Interval.Current/a.Duration;
        a.HitActive=alive&&next.Action==CharacterAction.Attack&&Overlap(from,to,d.HitStart,d.HitEnd);
        a.Invulnerable=alive&&next.Action==CharacterAction.Dodge&&Overlap(from,to,d.InvulnerableStart,d.InvulnerableEnd);
        if(next.Action==CharacterAction.Attack){Notify(a,d.HitStart,CombatEventKind.HitOpen);Notify(a,d.HitEnd,CombatEventKind.HitClose);}
        if(next.Action==CharacterAction.Dodge){Notify(a,d.InvulnerableStart,CombatEventKind.InvulnerableOpen);Notify(a,d.InvulnerableEnd,CombatEventKind.InvulnerableClose);}
        return next.Action is CharacterAction.Attack or CharacterAction.Dodge;
    }
    private double Duration(Actor a,CharacterAction action)=>_roots.Duration(a.Id,Clip(a,action));
    private static Guid Clip(Actor a,CharacterAction action)=>action switch {CharacterAction.Idle=>a.Definition.IdleClip,CharacterAction.Run=>a.Definition.RunClip,CharacterAction.Attack=>a.Definition.AttackClip,_=>a.Definition.DodgeClip};
    private void Notify(Actor a,float normalized,CombatEventKind kind)
    {
        double t=normalized*a.Duration,p=a.Interval.Previous,c=a.Interval.Current;
        if(c>p && (p<=t&&t<c || normalized==1&&p<t&&c==t))Event(a,kind);
    }
    internal void BeginStep()
    {
        if(_world!.Tick!=_tick)throw new InvalidOperationException("Action clock lost its committed quantum.");
        _events.Clear();foreach(var pair in _startupHealth)_health[pair.Key]=_world.FindObject(pair.Key).Get<HealthData>();
    }
    internal void StageHits(Func<Guid,float,uint,Guid> query)
    {
        foreach(var a in _actors.Values) {
            if(!a.HitActive)continue;
            Guid target=query(a.Id,a.Definition.Reach,a.Definition.HitMask);
            if(target==Guid.Empty||target==a.Id||!_health.TryGetValue(target,out var health)||!a.Candidate.HitTargets.Add(target))continue;
            if(a.Candidate.HitTargets.Count>64)throw new InvalidOperationException("Per-attack target budget exceeded.");
            bool blocked=_actors.TryGetValue(target,out var opponent)&&opponent.Invulnerable;
            float damage=blocked ? 0 : Math.Min(health.Current,a.Definition.Damage);
            _health[target]=health with{Current=health.Current-damage};Event(a,blocked?CombatEventKind.Blocked:CombatEventKind.Hit,target,damage);
        }
    }
    internal void Commit(World world)
    {
        if(world!=_world||world.Tick!=_tick+1)throw new InvalidOperationException("Action commit identity/tick mismatch.");
        foreach(var a in _actors.Values){a.Committed=a.Candidate;a.Requested=null;}
        _tick=world.Tick;_committedEvents=_events.ToArray();
    }
    internal ActionStatus Inspect(Guid id){var a=_actors[id];return new(id,a.Committed.Action,a.Committed.Instance,a.Committed.Combo,_roots.Time(id),_tick);}
    internal CombatEvent[] Events()=> (CombatEvent[])_committedEvents.Clone();
}
