namespace Ncma;

/// <summary>Semantic UI values, independent of platform, GUI, renderer and widget implementations.</summary>
public enum GameplayUiActionKind { Activate, ValueChanged, TextChanged }

/// <summary>A committed observation plus a host-issued, revocable UI binding. Never serialize this stamp.</summary>
public readonly record struct GameplayUiStamp(Guid SessionId, Guid WorldId, ulong SceneRevision, ulong Tick,
    Guid InstanceId, ulong Generation, Guid Admission);

/// <summary>Copied proposal, not a method name to invoke or a reference to a live widget/GameObject.</summary>
public readonly record struct GameplayUiAction(ulong Sequence, Guid ElementId, string Action,
    GameplayUiActionKind Kind, float Value = 0, string Text = "");

/// <summary>
/// Immutable bounded input shared by all behaviours/Systems in one fixed step. Applications filter by
/// action/element; reading does not consume it. The next committed step has no repeated edge events.
/// </summary>
public sealed class GameplayUiInput
{
    private readonly GameplayUiAction[] _actions;
    internal GameplayUiInput(GameplayUiStamp stamp, GameplayUiAction[] ownedActions)
    { Stamp = stamp; _actions = ownedActions; }
    public GameplayUiStamp Stamp { get; }
    public int Count => _actions.Length;
    public GameplayUiAction this[int index] => _actions[index];
    internal static GameplayUiInput Empty { get; } = new(default, []);
}
