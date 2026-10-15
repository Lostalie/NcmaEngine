using Ncma.Scene.Rendering;
namespace Ncma.Rendering.Scene;

internal sealed record PreparedPostView(RegisteredSceneShaders? Scene, RegisteredEnvironmentSceneShaders? Environment,
    PostProcessPlan? Plan, PreparedPostProcessPrograms? Programs);

// Borrowed file leases and renderer; host disposes this service before their owners.
// Whole library admission and bounded cached composition occur only at trusted off-frame boundaries.
public sealed class PostProcessRuntime : IDisposable
{
    private readonly RendererSession _renderer;
    private readonly RuntimeShaderFileSet _sceneFiles;
    private readonly RuntimePostProcessLibrary _library;
    private readonly PreparedPostProcessLibrary _prepared;
    private readonly PostProcessModules _modules;
    private readonly Func<bool> _allowed;
    private readonly int _owner = Environment.CurrentManagedThreadId;
    private readonly Dictionary<(PostProcessData,uint,uint), (PostProcessPlan?,PreparedPostProcessPrograms?)> _compositions = [];
    private bool _preparing, _disposed;
    public PostProcessRuntime(RendererSession renderer, RuntimeShaderFileSet sceneFiles, RuntimePostProcessLibrary library,
        PostProcessModules modules, Func<bool> preparationAllowed)
    {
        ArgumentNullException.ThrowIfNull(renderer); ArgumentNullException.ThrowIfNull(sceneFiles); ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(modules); ArgumentNullException.ThrowIfNull(preparationAllowed);
        _renderer=renderer;_sceneFiles=sceneFiles;_library=library;_modules=modules;_allowed=preparationAllowed;
        _prepared=library.Prepare(renderer,sceneFiles,Approved);
    }
    private void Verify()
    {
        if(_owner!=Environment.CurrentManagedThreadId)throw new InvalidOperationException("Post runtime owner thread.");
        ObjectDisposedException.ThrowIf(_disposed,this);
        _sceneFiles.Verify();_library.Verify();_renderer.VerifyShaderPreparation();
    }
    private bool Approved(){Verify();bool result=_allowed();Verify();return result;}
    internal void VerifyFor(RendererSession renderer)
    {
        if(!ReferenceEquals(renderer,_renderer))throw new ArgumentException("Foreign post runtime renderer.");
        Verify();if(_preparing)throw new InvalidOperationException("Post runtime preparation is nonreentrant.");
        _preparing=true;try{_prepared.VerifyFor(renderer);}finally{_preparing=false;}
    }
    internal PreparedPostView Prepare(PostProcessData settings,uint width,uint height,bool environment,bool shadows,bool skin,PreparedShadowLibrary? highShadows=null)
    {
        VerifyFor(_renderer);settings=_modules.Validate(settings);if(!settings.Enabled)throw new ArgumentException("Off requires original scene programs, not post preparation.");
        _preparing=true;
        try {
            var scene=environment?null:_prepared.SceneTone(_renderer,settings.ToneModule,shadows,skin).Scene;
            var env=environment?_prepared.EnvironmentTone(_renderer,settings.ToneModule,shadows,skin).Scene:null;
            if(highShadows is not null){
                if(!shadows)throw new ArgumentException("Unshadowed view cannot select High shadow programs.");
                var high=highShadows.Resolve(_library.Tone(settings.ToneModule,environment?ShaderProfile.SceneEnvironment:ShaderProfile.Scene3D,true,skin));
                scene=high.Scene;env=high.Environment;
            }
            var key=(settings,width,height);
            if(!_compositions.TryGetValue(key,out var value)) {
                ComposedPostProcess? composed;
                _renderer.BeginToneOperation();try { if(!Approved())throw new InvalidOperationException("Post composition approval revoked.");
                    composed=_modules.Build(settings,width,height,id=>_library.Program(id));
                    if(!Approved())throw new InvalidOperationException("Post composition approval revoked.");
                }finally{_renderer.EndToneOperation();}
                var programs=composed is null?null:PreparedPostProcessPrograms.Prepare(_renderer,composed.Programs,Approved);
                value=(composed?.Plan,programs);
                // Four immutable <=8MiB packages, no GPU ownership. Eviction never destroys a scene's copied binding.
                if(_compositions.Count==4)_compositions.Clear();
                _compositions.Add(key,value);
            }
            _prepared.VerifyFor(_renderer);
            return new(scene,env,value.Item1,value.Item2);
        }finally{_preparing=false;}
    }
    internal PostProcessData Validate(PostProcessData settings) { VerifyFor(_renderer);return _modules.Validate(settings); }
    internal bool BindingAllowed() => Approved();
    // The caller must retain every resource BEFORE a fallible binding/approval.
    // This gate never assumes ownership of a partially constructed candidate.
    internal void ApproveInstallation(Action prepare)
    {
        ArgumentNullException.ThrowIfNull(prepare);VerifyFor(_renderer);_preparing=true;
        try {
            prepare();
            _renderer.BeginToneOperation();try { if(!Approved())throw new InvalidOperationException("Post install approval revoked."); }finally{_renderer.EndToneOperation();}
        }finally{_preparing=false;}
    }
    public void Dispose() { if(_owner!=Environment.CurrentManagedThreadId)throw new InvalidOperationException("Post runtime owner thread.");if(_disposed)return;if(_preparing)throw new InvalidOperationException("Cannot close during post preparation.");_compositions.Clear();_disposed=true; }
}
