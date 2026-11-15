using Ncma.Scene;
#if NCMA_AI
using System.Diagnostics;
using System.Security.Cryptography;
using Ncma.Application;
using Ncma.Editor.Modules;
using Ncma.Modules;
#endif

namespace Ncma.Editor.App;

internal sealed unsafe partial class CandidatePresentation
{
    private bool SceneMeasurementMode =>
#if NCMA_AI
        _aiSceneInput is not null;
#else
        false;
#endif
    private void RequireHostComposition(ISceneContent snapshot)
    {
        RequireComposition(snapshot);
        if (SceneMeasurementMode && snapshot.Objects.Any(o => o.Behaviours.Length != 0 ||
            o.Components.Any(c => c.TypeId != "ncma.transform" || c.Version != 1)))
            throw new ArgumentException("ai_scene_measurement_transform_only");
    }
    private void InitializeAiSceneMeasurement()
    {
#if NCMA_AI
        if (_aiSceneInput is null) return;
        _editor!.Document.RestoreBytes(_aiSceneInput.CopySceneBytes());
        _aiSceneInitial = HashScene();
        _editor.Edit!.Resynchronize();
        _editor.ConfigureDomainProposals(_aiSceneInput.Request.Inputs.Identity.RunId);
#endif
    }
#if NCMA_AI
    private PreparedAiSceneMeasurement? _aiSceneInput = null;
    private string? _aiSceneInitial, _aiSceneFinal;
    internal void ConfigureAiSceneMeasurement(PreparedAiSceneMeasurement input)
    {
        VerifyGraphicsOwner(); ArgumentNullException.ThrowIfNull(input);
#if NCMA_EXTENSIONS
        throw new InvalidOperationException("ai_scene_measurement_ai_only");
#else
        if (project is not null || !cpuOnly || preview || _aiSceneInput is not null)
            throw new InvalidOperationException("ai_scene_measurement_host_mode");
        var id = input.Request.Inputs.Identity;
        ConfigureAiMeasurement(new(id.RunId, id.SourceSha256, id.WorkloadSha256,
            id.Configuration == MeasurementConfiguration.Debug ? ObservationConfiguration.Debug : ObservationConfiguration.Release,
            ObservationVariant.Ai, Stopwatch.Frequency), input.Request.Recipient.Limits());
        _aiSceneInput = input; // Detached source only; caller retains the actual input lease until original close.
#endif
    }
    private string HashScene() => Convert.ToHexString(SHA256.HashData(_editor!.Document.CaptureBytes()));
    private void CaptureAiSceneMeasurementFinal()
    { if (_aiSceneInitial is not null && _editor is not null) _aiSceneFinal = HashScene(); }
    internal (string? InitialSha256, string? FinalSha256) AiSceneMeasurementState
    { get { VerifyGraphicsOwner(); return (_aiSceneInitial, _aiSceneFinal); } }
#endif
}
