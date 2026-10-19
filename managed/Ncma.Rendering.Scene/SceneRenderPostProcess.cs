using Ncma.Scene.Rendering;
namespace Ncma.Rendering.Scene;

public sealed partial class SceneRenderSession
{
    private PostProcessRuntime? _postRuntime;
    private PostProcessData _postPrepared=PostProcessData.Off,_postObserved=PostProcessData.Off;
    private EnvironmentLightingData _postEnvironment=EnvironmentLightingData.Off;
    private ulong _postObservedRevision;
    private bool _postReady,_postViewEmpty;
    private float _postInputExposure;
    public PostProcessData PreparedPostProcess { get { Verify();return _postPrepared; } }
    public bool IsPostProcessReady { get { Verify();return !_resourceRecoveryRequired&&!_closing&&_postObservedRevision==_world.Revision&&(_postRuntime is null?!_postObserved.Enabled:
        _postReady&&!_postViewEmpty&&_postPrepared==_postObserved&&_postEnvironment==_environment); } }
    private void InitializePostProcess(PostProcessRuntime? runtime,PostProcessData startup)
    {
        _postRuntime=runtime;var current=ScenePostProcessState.Read(_postReader,WorldId);
        if(startup!=current)throw new ArgumentException("Stale startup post-process settings.");
        if(runtime is null&&current.Enabled)throw new ArgumentException("Enabled post processing requires explicit trusted runtime.");
        if(runtime is not null){using var read=SceneEnvironmentState.BeginRead(_world,WorldId);runtime.VerifyFor(_renderer);_=runtime.Validate(current);}
        _postObserved=current;_postObservedRevision=_world.Revision;
    }
    private void VerifyPreparedPostProcess()
    {
        if(_postObservedRevision!=_world.Revision){_postObserved=ScenePostProcessState.Read(_postReader,WorldId);_postObservedRevision=_world.Revision;}
        if(_postRuntime is null){if(_postObserved.Enabled)throw new InvalidOperationException("Post runtime must be prepared before enabling post processing.");return;}
        if(_hasGeometry&&!_postViewEmpty&&(!_postReady||_postObserved!=_postPrepared||_postEnvironment!=_environment))throw new InvalidOperationException("Post view requires explicit off-frame preparation.");
    }
    private void PreparePostView(uint width,uint height,Guid sceneCamera,SceneCameraView? browserCamera,float exposure,float ambient)
    {
        using var read=SceneEnvironmentState.BeginRead(_world,WorldId);
        if(_environmentPreparing)throw new InvalidOperationException("Nonreentrant scene post preparation.");
        var runtime=_postRuntime!;_environmentPreparing=true;
        try {
            runtime.VerifyFor(_renderer);
            var value=runtime.Validate(ScenePostProcessState.Read(_postReader,WorldId));
            var selection=ObserveShadowSelection();
            var view=_extractor.Extract(_resources.Metadata,browserCamera is null?sceneCamera:Guid.Empty,width,height,browserCamera,_animation is not null);
            // A culled/missing-camera view has no installed candidate. Do not stamp old GPU state
            // as the newly requested settings; returning geometry must prepare it first.
            if(view.Camera is not {} camera||view.Geometry.Count==0&&view.ShadowCasters.Count==0){_postObserved=value;_postObservedRevision=_world.Revision;_postViewEmpty=true;_postReady=false;return;}
            bool shadows=SceneShadowPipelinePolicy.EffectiveShadows(selection,view.PrimaryLight is {Data.CastShadow:true});uint w=Math.Max(1,(uint)(width*camera.Data.ViewportWidth)),h=Math.Max(1,(uint)(height*camera.Data.ViewportHeight));
            float effective=value.Enabled?exposure*SceneTonePresets.ExposureFromStops(value.ExposureEv):exposure;
            if(!float.IsFinite(effective)||effective is <.01f or >16)throw new ArgumentException("Combined Tone exposure must remain within the existing [.01,16] contract.");
            if(_pipeline is not null&&_postReady&&value==_postPrepared&&_postEnvironment==_environment&&_width==w&&_height==h&&_shadows==shadows&&_postInputExposure==exposure&&_ambient==ambient&&_shadowPrepared==selection){_postObserved=value;_postObservedRevision=_world.Revision;CommitShadowSelection(selection);return;}
            var high=shadows&&selection?.Quality==Ncma.Assets.SceneShadowQuality.High?(_highShadows??throw new InvalidOperationException("Checked High shadow library required.")):null;
            PreparedPostView? post=value.Enabled?runtime.Prepare(value,w,h,_environmentShaders is not null,shadows,_hasSkin,high):null;
            BeginCandidateBundle();
            try {
                runtime.ApproveInstallation(()=>{
                        var pipeline=CreateSelectedPipeline(effective,ambient,shadows,selection,_environmentShaders is null?ShaderProfile.Scene3D:ShaderProfile.SceneEnvironment);
                        if(_environmentShaders is not null) {
                            _pendingPipeline=new(_renderer,pipeline,w,h,environmentShaders:post?.Environment??SelectShadowPrograms(null,shadows?_environmentShaders.Shadowed:_environmentShaders.Unshadowed,shadows,selection).Environment);
                            _pendingPipeline.ConfigureEnvironment(_environmentGpu?.Resource,_environment.ToConfiguration(),EnvironmentApproved);
                        } else _pendingPipeline=new(_renderer,pipeline,w,h,shaders:post?.Scene??SelectShadowPrograms(shadows?_shaders!.Shadowed:_shaders!.Unshadowed,null,shadows,selection).Scene);
                        if(post?.Plan is not null)_pendingPipeline.ConfigurePostProcess(post.Plan,post.Programs,runtime.BindingAllowed);
                });
                // Complete scene + environment + post candidate before releasing installed output.
                InstallPendingPipeline();
                _width=w;_height=h;_shadows=shadows;_exposure=effective;_ambient=ambient;_postInputExposure=exposure;
                _postPrepared=value;_postObserved=value;_postObservedRevision=_world.Revision;_postEnvironment=_environment;_postViewEmpty=false;_postReady=true;
                CommitShadowSelection(selection);
            }catch(Exception failure){AbandonCandidate(failure);throw;}
        }finally{_environmentPreparing=false;}
    }
}
