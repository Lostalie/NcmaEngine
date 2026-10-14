using Ncma.Gameplay;
using Ncma.Ui;

namespace Ncma.Application.Runtime;

public sealed record SceneUiAction(string Action, GameplayUiActionKind Kind);

// Immutable trusted application startup configuration, never a project/Agent-supplied executable
// callback. Pure UI/rendering stay independent of gameplay; only this composition layer maps them.
public sealed class SceneUiActions
{
    private readonly Dictionary<string, GameplayUiActionKind> _routes = new(StringComparer.Ordinal);
    public string[] RegisteredActions => _routes.Keys.Order(StringComparer.Ordinal).ToArray();
    public SceneUiActions(params SceneUiAction[] routes)
    {
        ArgumentNullException.ThrowIfNull(routes);
        if (routes.Length > PlaySession.MaxUiActions) throw new ArgumentException("UI route budget exceeded.");
        foreach (var route in routes) {
            if (route is null || string.IsNullOrEmpty(route.Action) || route.Action.Length > 96 ||
                route.Action.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-')) ||
                !Enum.IsDefined(route.Kind) || !_routes.TryAdd(route.Action, route.Kind))
                throw new ArgumentException("Unique bounded UI action identifiers and kinds required.");
        }
    }
    public void Validate(UiDefinition definition)
    {
        UiCodec.Validate(definition);
        foreach (var element in definition.Elements) {
            GameplayUiActionKind? kind = element.Kind switch {
                UiKind.Button => GameplayUiActionKind.Activate,
                UiKind.Slider => GameplayUiActionKind.ValueChanged,
                UiKind.TextInput => GameplayUiActionKind.TextChanged,
                _ => null
            };
            if (kind is { } value) {
                if (!_routes.TryGetValue(element.Action, out var registered) || registered != value)
                    throw new ArgumentException("Every interactive HUD action requires an exact trusted startup route.");
                if (element.Kind == UiKind.TextInput && element.Font == Guid.Empty)
                    throw new ArgumentException("A text input needs an explicit font even when initially empty.");
            } else if (element.Action.Length != 0) throw new ArgumentException("Passive/scroll UI elements cannot dispatch actions.");
        }
    }
    public void Install(PlaySession play)
    {
        ArgumentNullException.ThrowIfNull(play);
        play.RegisterUiActions(_routes.ToArray());
    }
    internal void RequireInstalled(PlaySession play)
    {
        foreach (var route in _routes)
            if (!play.AcceptsUiAction(route.Key, route.Value)) throw new InvalidOperationException("UI routes were not installed before gameplay startup.");
    }
    internal GameplayUiAction Map(UiRuntime runtime, UiAction action)
    {
        var element = runtime.Element(action.Element);
        if (element.Action != action.Action || !_routes.TryGetValue(action.Action, out var kind))
            throw new ArgumentException("Foreign UI element/action.");
        bool supported = element.Kind switch {
            UiKind.Button => kind == GameplayUiActionKind.Activate && action.Kind is UiInputKind.PointerUp or UiInputKind.Activate,
            UiKind.Slider => kind == GameplayUiActionKind.ValueChanged && action.Kind is UiInputKind.PointerDown or UiInputKind.PointerMove,
            UiKind.TextInput => kind == GameplayUiActionKind.TextChanged && action.Kind is UiInputKind.Text or UiInputKind.Backspace,
            _ => false
        };
        if (!supported) throw new ArgumentException("UI input kind does not match its declared route.");
        return new(action.Sequence, action.Element, action.Action, kind, action.Value, action.Text);
    }
}

// Copied events only. Host calls Observe BEFORE platform input, Submit BEFORE Advance, and Cancel
// on lost capture/workspace/focus. No renderer calls, live World writes, method dispatch or IPC.
public sealed class SceneUiInput : IDisposable
{
    private readonly UiRuntime _runtime;
    private readonly PlaySession _play;
    private readonly SceneUiActions _routes;
    private readonly GameplayUiAction[] _buffer = new GameplayUiAction[PlaySession.MaxUiActions];
    private GameplayUiStamp _observation;
    private UiStamp _uiStamp;
    private Guid _admission;
    private bool _disposed, _observed;

    public SceneUiInput(UiRuntime runtime, PlaySession play, SceneUiActions routes)
    {
        ArgumentNullException.ThrowIfNull(runtime); ArgumentNullException.ThrowIfNull(play); ArgumentNullException.ThrowIfNull(routes);
        routes.Validate(runtime.CaptureDefinition()); routes.RequireInstalled(play);
        _runtime = runtime; _play = play; _routes = routes;
    }
    private void Verify() { _ = _runtime.Stamp; ObjectDisposedException.ThrowIf(_disposed, this); }

    public bool Observe()
    {
        Verify();
        bool active = _play.TryGetUiInputStamp(out var current);
        if (_play.State != PlayState.Running) { Cancel(); return false; }
        if (active && current.Admission != _admission)
            throw new InvalidOperationException("Another producer owns this Play UI binding.");
        var ui = _runtime.Stamp;
        bool changed = ui.Instance != _uiStamp.Instance || ui.Generation != _uiStamp.Generation ||
            ui.PlaySession != _play.SessionId || ui.World != _play.Document.World.Identity;
        // A normal fixed step increments scene revision once. Out-of-step metadata/world changes
        // revoke queued observations, while normal ticks preserve widget focus and pointer capture.
        bool externalChange = active && _observed && (current.Tick < _observation.Tick ||
            current.SceneRevision < _observation.SceneRevision ||
            current.SceneRevision - _observation.SceneRevision != current.Tick - _observation.Tick);
        if (!active || changed || externalChange) {
            Cancel();
            _runtime.Bind(_play.SessionId, _play.Document.World.Identity, _play.Tick);
            ui = _runtime.Stamp; current = _play.BindUiInput(ui.Instance, ui.Generation); _admission = current.Admission;
        } else _runtime.Bind(current.SessionId, current.WorldId, current.Tick);
        _observation = current; _uiStamp = _runtime.Stamp; _observed = true; return true;
    }
    public void Submit(ReadOnlySpan<UiAction> actions)
    {
        Verify();
        if (!_observed || !_play.TryGetUiInputStamp(out var current) || current != _observation || _runtime.Stamp != _uiStamp)
            throw new InvalidOperationException("UI observation changed before dispatch.");
        if (actions.Length > _buffer.Length) throw new ArgumentException("UI dispatch batch exceeds capacity.");
        try {
            for (int i = 0; i < actions.Length; i++) {
                if (actions[i].Stamp != _uiStamp) throw new ArgumentException("Stale or foreign UI proposal.");
                _buffer[i] = _routes.Map(_runtime, actions[i]);
            }
            _play.SubmitUiActions(_observation, _buffer.AsSpan(0, actions.Length));
        } finally { Array.Clear(_buffer); }
    }
    public void Cancel()
    {
        Verify();
        if (_play.TryGetUiInputStamp(out var current) && current.Admission == _admission) _play.CancelUiInput();
        _runtime.CancelInput(); _admission = Guid.Empty; _observed = false; Array.Clear(_buffer);
    }
    public void Dispose()
    {
        if (_disposed) { _ = _runtime.Stamp; return; }
        Cancel(); _disposed = true;
    }
}
