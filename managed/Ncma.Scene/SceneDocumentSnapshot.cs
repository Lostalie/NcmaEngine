using System.Text.Json.Serialization;
using Ncma.Runtime;
namespace Ncma.Scene;

public enum ExportKind : uint { Float = 1, Double = 2, Integer = 3, Boolean = 4 }
public sealed record ExportData(
    [property: JsonRequired] string Name,
    [property: JsonRequired] ExportKind Kind,
    [property: JsonRequired] double Value);
public sealed record BehaviourBindingData(
    [property: JsonRequired] Guid Id,
    [property: JsonRequired] string TypeName,
    [property: JsonRequired] bool Enabled,
    [property: JsonRequired] ExportData[] Exports);
public sealed record SceneObjectData(
    [property: JsonRequired] Guid Id,
    [property: JsonRequired] string Name,
    [property: JsonRequired] ComponentSnapshot[] Components,
    [property: JsonRequired] BehaviourBindingData[] Behaviours);
public sealed record SceneDocumentSnapshot(
    [property: JsonRequired] int Version,
    [property: JsonRequired] string Name,
    [property: JsonRequired] SceneObjectData[] Objects);
