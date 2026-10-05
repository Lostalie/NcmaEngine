using System.Text.Json;
using Ncma.Runtime;
namespace Ncma.Animation;

// Persistent settings only. Pose arrays, resource handles, clock/debt and World references are not serialized.
public readonly record struct ClipPlaybackData(Guid ClipId, bool Playing, bool Loop, double Speed, double StartTime) : IComponent
{
    public const string TypeId = "ncma.animation.clip_playback";
    public static ClipPlaybackData Validate(ClipPlaybackData value)
    {
        if(value.ClipId==Guid.Empty || !double.IsFinite(value.Speed) || value.Speed is <0 or >8 ||
            !double.IsFinite(value.StartTime) || value.StartTime is <0 or >600) throw new ArgumentException("Invalid persistent clip playback settings.");
        return value;
    }
    public static ComponentRegistry Register(ComponentRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        string schema=JsonSerializer.Serialize(new{type="object",additionalProperties=false,
            required=new[]{"clipId","playing","loop","speed","startTime"},properties=new{
                clipId=new{type="string"},playing=new{type="boolean"},loop=new{type="boolean"},speed=new{type="number"},startTime=new{type="number"}}});
        registry.Register<ClipPlaybackData>(TypeId,1,schema,Validate);return registry;
    }
}
public readonly record struct ClipSampleTimes(double Previous, double Current, float Alpha);
// One clock per object, C# only. Invoked AFTER each successful fixed-step commit, not by OnUpdate/native.
public sealed class ClipClock
{
    private readonly int _owner=Environment.CurrentManagedThreadId;
    private readonly Guid _world;
    private ulong _tick;
    private ClipPlaybackData _settings;
    private double _duration, _previous, _current;
    public ClipClock(World world, ClipPlaybackData settings, double duration)
    { ArgumentNullException.ThrowIfNull(world);if(world.IsUpdating)throw new InvalidOperationException("Prepare animation clocks at a committed boundary.");_world=world.Identity;_tick=world.Tick;Validate(settings,duration);_settings=settings;_duration=duration;_previous=_current=settings.StartTime; }
    public ulong CommittedTick { get{Verify();return _tick;} }
    public double UnwrappedTime { get{Verify();return _current;} }
    public void ObserveCommitted(World world, ClipPlaybackData settings, double duration, double fixedDelta)
    {
        Verify();ArgumentNullException.ThrowIfNull(world);Validate(settings,duration);
        if(world.IsUpdating)throw new InvalidOperationException("Animation time observes only committed fixed-step boundaries.");
        if(world.Identity!=_world || world.Tick<_tick || world.Tick-_tick>128 || !double.IsFinite(fixedDelta) || fixedDelta is <.001 or >1)
            throw new ArgumentException("Clock requires the original World and bounded committed fixed steps.");
        ulong delta=world.Tick-_tick;
        if(delta==0 && settings==_settings && duration==_duration)return;
        double previous=_current,current=_current;
        // Explicit clip/start changes reset at their committed boundary. Speed/pause/loop changes retain time.
        if(settings.ClipId!=_settings.ClipId || settings.StartTime!=_settings.StartTime || duration!=_duration) previous=current=settings.StartTime;
        if(delta!=0 && settings.Playing) {
            previous=current+(delta-1)*fixedDelta*settings.Speed;current+=delta*fixedDelta*settings.Speed;
            if(!settings.Loop){previous=Math.Min(duration,previous);current=Math.Min(duration,current);}
        } else previous=current;
        if(!double.IsFinite(current) || !double.IsFinite(previous))throw new ArgumentException("Animation time overflow.");
        _previous=previous;_current=current;_settings=settings;_duration=duration;_tick=world.Tick;
    }
    public ClipSampleTimes Sample(float alpha, bool paused)
    {
        Verify();if(!float.IsFinite(alpha) || alpha is <0 or >1)throw new ArgumentException("Animation interpolation alpha range.");
        return new(Wrap(_previous),Wrap(_current),paused || !_settings.Playing ? 1 : alpha);
    }
    private double Wrap(double time) => _settings.Loop ? time%_duration : Math.Min(_duration,time);
    private static void Validate(ClipPlaybackData settings,double duration)
    { _=ClipPlaybackData.Validate(settings);if(!double.IsFinite(duration) || duration is <=0 or >600 || settings.StartTime>duration)throw new ArgumentException("Clip start/time does not match immutable clip duration."); }
    private void Verify(){if(_owner!=Environment.CurrentManagedThreadId)throw new InvalidOperationException("Animation clock requires owner thread.");}
}
