namespace Ncma;

/// <summary>Main gameplay extension point. The engine owns lifecycle invocation.</summary>
public abstract class Behaviour
{
    public Node Node { get; internal set; } = null!;

    protected internal virtual void OnCreate() { }
    protected internal virtual void OnEnable() { }
    protected internal virtual void OnUpdate(double deltaSeconds) { }
    protected internal virtual void OnFixedUpdate(double fixedDeltaSeconds) { }
    protected internal virtual void OnDisable() { }
    protected internal virtual void OnDestroy() { }

    internal void DispatchCreate() => OnCreate();
    internal void DispatchEnable() => OnEnable();
    internal void DispatchUpdate(double deltaSeconds) => OnUpdate(deltaSeconds);
    internal void DispatchFixedUpdate(double fixedDeltaSeconds) => OnFixedUpdate(fixedDeltaSeconds);
    internal void DispatchDisable() => OnDisable();
    internal void DispatchDestroy() => OnDestroy();
}

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class ExportAttribute : Attribute
{
    public string? DisplayName { get; init; }
    public string? Category { get; init; }
}
