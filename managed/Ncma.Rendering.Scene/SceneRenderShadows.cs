using Ncma.Assets;
using Ncma.Scene;
using Ncma.Scene.Rendering;

namespace Ncma.Rendering.Scene;

public sealed partial class SceneRenderSession
{
    private PreparedShadowLibrary? _highShadows;
    private SceneShadowData? _shadowObserved,_shadowPrepared;
    private ulong _shadowObservedRevision;
    private bool _shadowReady;
    public SceneShadowData? PreparedShadowSelection { get { Verify();return _shadowPrepared; } }
    private void InitializeShadows(ISceneContent startup,PreparedShadowLibrary? high)
    {
        var selection=SceneShadowState.Read(startup);
        if(selection!=SceneShadowState.Read(_shadowReader,WorldId))throw new ArgumentException("Stale startup shadow selection.");
        if(_hasGeometry&&selection?.Quality==SceneShadowQuality.High&&high is null)throw new ArgumentException("High scene shadows require an explicitly admitted source-free library.");
        _highShadows=high;_shadowObserved=_shadowPrepared=selection;_shadowObservedRevision=_world.Revision;
    }
    private SceneShadowData? ObserveShadowSelection()
    {
        SceneEnvironmentState.VerifyBoundary(_world,WorldId);
        if(_shadowObservedRevision!=_world.Revision){var current=SceneShadowState.Read(_shadowReader,WorldId);_shadowObserved=current;_shadowObservedRevision=_world.Revision;}
        return _shadowObserved;
    }
    private void VerifyPreparedShadows()
    {
        var selection=ObserveShadowSelection();
        if(selection!=_shadowPrepared||selection is not null&&!_shadowReady)throw new InvalidOperationException("Shadow selection requires explicit off-frame preparation.");
    }
    private void CommitShadowSelection(SceneShadowData? selection){_shadowPrepared=selection;_shadowReady=true;}
    private RenderPipeline CreateSelectedPipeline(float exposure,float ambient,bool shadows,SceneShadowData? selection,ShaderProfile profile) =>
        SceneShadowPipelinePolicy.Create(_hasCustomPipeline?_pipelineFactory:null,exposure,ambient,shadows,selection,profile);
    private PreparedShadowPrograms SelectShadowPrograms(RuntimeShaderPreparation? scene,EnvironmentShaderPreparation? environment,bool shadows,SceneShadowData? selection)
    {
        if(shadows&&selection?.Quality==SceneShadowQuality.High)
            return (_highShadows??throw new InvalidOperationException("Checked High shadow library missing; no fallback."))
                .Resolve(scene?.Package??environment?.Package??throw new InvalidOperationException("Missing exact base scene shader package."));
        return new(scene?.Scene,environment?.Scene);
    }
}
