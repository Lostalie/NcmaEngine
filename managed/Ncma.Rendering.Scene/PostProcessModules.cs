using System.Collections.ObjectModel;
using Ncma.Assets;
using Ncma.Scene.Rendering;
namespace Ncma.Rendering.Scene;

public sealed record PostParameterRule(string Name, float Minimum, float Maximum, bool Integer = false, float? Default = null);
public sealed record PostFeatureRegistration(Guid ModuleId, int Version, int Order,
    PostParameterRule[] Parameters, Guid[] Requires,
    Func<uint, uint, IReadOnlyDictionary<string, float>, PostProcessPackage, PostProcessPlan?> Build, string? Name = null);
public sealed record PostToneRegistration(Guid ModuleId, int Version, string? Name = null);
public sealed record PostModuleDescription(Guid ModuleId, int Version, string Kind, string Name, int Order,
    PostParameterRule[] Parameters, Guid[] Requires);
public sealed record ComposedPostProcess(PostProcessPlan Plan, PostProcessPackage Programs);

// Trusted startup code only: delegates are neither serialized nor loaded from configuration.
// Official modules go through exactly this registry and public numerical graph contract.
public sealed class PostProcessModules
{
    private readonly Dictionary<Guid, PostFeatureRegistration> _features = [];
    private readonly Dictionary<Guid, PostToneRegistration> _tones = [];
    private readonly int _owner = Environment.CurrentManagedThreadId;
    private bool _building;
    public PostProcessModules(IEnumerable<PostToneRegistration> tones, IEnumerable<PostFeatureRegistration> features)
    {
        ArgumentNullException.ThrowIfNull(tones); ArgumentNullException.ThrowIfNull(features);
        foreach (var tone in tones.Take(33))
            if (tone is null || tone.ModuleId == Guid.Empty || tone.Version < 1 || !ValidLabel(tone.Name) || _tones.Count == 32 || !_tones.TryAdd(tone.ModuleId, tone))
                throw new ArgumentException("Post Tone registration.");
        foreach (var feature in features.Take(33)) {
            if (feature is null || feature.ModuleId == Guid.Empty || feature.Version < 1 || !ValidLabel(feature.Name) || feature.Order is < 0 or > 1024 ||
                feature.Parameters is null || feature.Parameters.Length > 16 || feature.Requires is null || feature.Requires.Length > 8 || feature.Build is null ||
                _features.Count == 32 || _tones.ContainsKey(feature.ModuleId) || _features.ContainsKey(feature.ModuleId)) throw new ArgumentException("Post Feature registration.");
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var rule in feature.Parameters)
                if (rule is null || !PostProcessData.ValidName(rule.Name) || !names.Add(rule.Name) || !float.IsFinite(rule.Minimum) || !float.IsFinite(rule.Maximum) ||
                    rule.Minimum < -65504 || rule.Maximum > 65504 || rule.Minimum > rule.Maximum || rule.Integer && (rule.Minimum != MathF.Truncate(rule.Minimum) || rule.Maximum != MathF.Truncate(rule.Maximum)) ||
                    rule.Default is {} d&&(!float.IsFinite(d)||d<rule.Minimum||d>rule.Maximum||rule.Integer&&d!=MathF.Truncate(d)))
                    throw new ArgumentException("Post parameter registration.");
            if (feature.Requires.Any(id => id == Guid.Empty || id == feature.ModuleId) || feature.Requires.Distinct().Count() != feature.Requires.Length)
                throw new ArgumentException("Post dependency registration.");
            _features.Add(feature.ModuleId, feature with { Parameters = feature.Parameters.ToArray(), Requires = feature.Requires.ToArray() });
        }
        foreach (var f in _features.Values) if (f.Requires.Any(id => !_features.ContainsKey(id) || _features[id].Order > f.Order)) throw new ArgumentException("Unregistered/inverted post dependency.");
        var visiting = new HashSet<Guid>(); var visited = new HashSet<Guid>();
        void Visit(Guid id) { if (visited.Contains(id)) return; if (!visiting.Add(id)) throw new ArgumentException("Post module dependency cycle."); foreach (var d in _features[id].Requires) Visit(d); visiting.Remove(id); visited.Add(id); }
        foreach (var id in _features.Keys) Visit(id);
    }
    private void Verify() { if (_owner != Environment.CurrentManagedThreadId) throw new InvalidOperationException("Post module owner thread."); if (_building) throw new InvalidOperationException("Nonreentrant post module build."); }
    private static bool ValidLabel(string? name)=>name is null||name.Length is >0 and <=64&&!name.Any(char.IsControl);
    public PostModuleDescription[] Describe()
    {
        Verify();return _tones.Values.Select(t=>new PostModuleDescription(t.ModuleId,t.Version,"tone",t.Name??t.ModuleId.ToString("D"),0,[],[]))
            .Concat(_features.Values.Select(f=>new PostModuleDescription(f.ModuleId,f.Version,"feature",f.Name??f.ModuleId.ToString("D"),f.Order,f.Parameters.ToArray(),f.Requires.ToArray())))
            .OrderBy(m=>m.Kind,StringComparer.Ordinal).ThenBy(m=>m.Order).ThenBy(m=>m.ModuleId).ToArray();
    }
    public PostProcessData Validate(PostProcessData settings)
    {
        Verify(); settings = PostProcessData.Validate(settings);
        if (!_tones.TryGetValue(settings.ToneModule, out var tone) || tone.Version != settings.ToneVersion) throw new ArgumentException("Unknown exact Tone module/version.");
        var seen = new HashSet<Guid>(); int order = -1;
        foreach (var feature in PostProcessData.DecodeStack(settings.StackJson)) {
            if (!_features.TryGetValue(feature.ModuleId, out var module) || module.Version != feature.Version || module.Order < order || module.Requires.Any(id => !seen.Contains(id)))
                throw new ArgumentException("Unknown exact Feature/version or invalid order/dependency.");
            if (feature.Parameters.Length != module.Parameters.Length) throw new ArgumentException("Exact registered parameter set required.");
            foreach (var rule in module.Parameters) {
                var p = feature.Parameters.SingleOrDefault(p => p.Name == rule.Name);
                if (p is null || p.Value < rule.Minimum || p.Value > rule.Maximum || rule.Integer && p.Value != MathF.Truncate(p.Value)) throw new ArgumentException("Registered post parameter range/type.");
            }
            seen.Add(feature.ModuleId); order = module.Order;
        }
        return settings;
    }
    public ComposedPostProcess? Build(PostProcessData settings, uint width, uint height, Func<Guid, PostProcessPackage> program)
    {
        settings = Validate(settings); ArgumentNullException.ThrowIfNull(program);
        if (width is 0 or > 4096 || height is 0 or > 4096) throw new ArgumentException("Post viewport budget.");
        if (!settings.Enabled) return null;
        _building = true;
        try {
            var plans = new List<(PostProcessPlan Plan, PostProcessPackage Programs)>();
            foreach (var feature in PostProcessData.DecodeStack(settings.StackJson)) {
                var package = program(feature.ModuleId) ?? throw new ArgumentException("Missing exact Feature program.");
                var parameters = new ReadOnlyDictionary<string, float>(feature.Parameters.ToDictionary(p => p.Name, p => p.Value, StringComparer.Ordinal));
                var plan = _features[feature.ModuleId].Build(width, height, parameters, package);
                if (plan is not null) plans.Add((plan, package));
            }
            if (plans.Count == 0) return null;
            var programs = PostProcessComposition.Programs(plans.Select(p => p.Programs));
            return new(PostProcessComposition.Chain(width, height, plans, programs), programs);
        } finally { _building = false; }
    }
    public static PostProcessModules Official() => new(
        [new(PostProcessLibrarySelection.AcesModule,1,"ACES fitted"),new(PostProcessLibrarySelection.ReinhardModule,1,"Reinhard"),new(PostProcessLibrarySelection.LinearModule,1,"Linear Clamp")],
        [new(PostProcessLibrarySelection.BloomModule, 1, 100,
            [new("levels",1,6,true,4),new("threshold",0,16,Default:1),new("softKnee",0,1,Default:.5f),new("intensity",0,4,Default:.08f),new("scatter",0,1,Default:.7f)], [],
            (w,h,p,bytes) => BloomFeature.Build(w,h,new(Levels:(int)p["levels"],Threshold:p["threshold"],SoftKnee:p["softKnee"],Intensity:p["intensity"],Scatter:p["scatter"]),bytes),"Bloom")]);
    public static PostFeatureInput Bloom(BloomSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings); settings.Validate();
        if (!settings.Enabled) throw new ArgumentException("Disabled Feature is omitted from the stack.");
        return new(PostProcessLibrarySelection.BloomModule,1,[new("levels",settings.Levels),new("threshold",settings.Threshold),new("softKnee",settings.SoftKnee),new("intensity",settings.Intensity),new("scatter",settings.Scatter)]);
    }
}
