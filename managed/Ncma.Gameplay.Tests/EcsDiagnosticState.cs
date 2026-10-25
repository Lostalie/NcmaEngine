using Ncma.Scene;

// Cold diagnostic result only. Authoring has no simulation tick; runtime evidence encodes
// the actual runtime snapshot. This DTO is not a mirrored store, checkpoint or permission.
internal sealed record EcsDiagnosticState(string Format, ulong? Tick, byte[] Bytes)
{
    internal static EcsDiagnosticState FromAuthoring(SceneDocument document) =>
        new("ncma.authoring-scene", null, document.CaptureBytes());
    internal static EcsDiagnosticState FromRuntime(RuntimeSceneInstance scene) =>
        new(RuntimeSceneImage.Format, scene.World.Tick, RuntimeSceneCodec.Encode(scene.CaptureSnapshot()));
}
