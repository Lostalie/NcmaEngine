namespace Ncma.Gameplay;

public sealed partial class PlaySession
{
    public const int MaxUiActions = 128, MaxUiTextLength = 16384, MaxUiBatchTextLength = 65536;
    private Dictionary<string, GameplayUiActionKind> _uiActions = new(StringComparer.Ordinal);
    private bool _uiRegistrationFrozen;
    private Guid _uiInstance, _uiAdmission;
    private ulong _uiGeneration, _uiSequence;
    private GameplayUiInput _pendingUiInput = GameplayUiInput.Empty;
    private GameplayUiInput? _stepUiInput;

    public GameplayUiInput UiInput { get { Verify(); return _stepUiInput ?? GameplayUiInput.Empty; } }

    // Trusted C# startup only. No callbacks, reflection dispatch or Agent registration endpoint.
    public void RegisterUiAction(string action, GameplayUiActionKind kind)
    {
        Control();
        if (_uiRegistrationFrozen || State != PlayState.Stopped)
            throw new InvalidOperationException("Register UI actions before the first Play start.");
        ValidateUiActionName(action);
        if (!Enum.IsDefined(kind) || _uiActions.Count == MaxUiActions || !_uiActions.TryAdd(action, kind))
            throw new ArgumentException("Duplicate, invalid or excessive UI action registration.");
    }

    public bool AcceptsUiAction(string action, GameplayUiActionKind kind)
    { Verify(); ValidateUiActionName(action); return _uiActions.TryGetValue(action, out var registered) && registered == kind; }

    public void RegisterUiActions(ReadOnlySpan<KeyValuePair<string, GameplayUiActionKind>> routes)
    {
        Control();
        if (_uiRegistrationFrozen || State != PlayState.Stopped)
            throw new InvalidOperationException("Register UI actions before the first Play start.");
        if (routes.Length > MaxUiActions - _uiActions.Count) throw new ArgumentException("UI route budget exceeded.");
        var candidate = new Dictionary<string, GameplayUiActionKind>(_uiActions, StringComparer.Ordinal);
        foreach (var route in routes) {
            ValidateUiActionName(route.Key);
            if (!Enum.IsDefined(route.Value) || !candidate.TryAdd(route.Key, route.Value))
                throw new ArgumentException("Duplicate or invalid UI action registration.");
        }
        _uiActions = candidate;
    }

    // One producer per Play; a replacement, focus reset, pause or reload revokes the previous ticket.
    // A ticket is an in-process lifecycle guard, not a sandbox against trusted C# code.
    public GameplayUiStamp BindUiInput(Guid instanceId, ulong generation)
    {
        Control();
        if (State != PlayState.Running) throw new InvalidOperationException("UI input binding requires running Play.");
        if (instanceId == Guid.Empty || generation == 0) throw new ArgumentException("Exact UI instance/generation required.");
        CancelUiInputCore();
        _uiInstance = instanceId; _uiGeneration = generation; _uiAdmission = Guid.NewGuid();
        return CurrentUiInputStamp;
    }

    public GameplayUiStamp CurrentUiInputStamp
    {
        get {
            Control();
            if (State != PlayState.Running || _uiAdmission == Guid.Empty)
                throw new InvalidOperationException("Bind current running UI before observing input.");
            return UiStamp();
        }
    }

    public bool TryGetUiInputStamp(out GameplayUiStamp stamp)
    {
        Control(); stamp = default;
        if (State != PlayState.Running || _uiAdmission == Guid.Empty) return false;
        stamp = UiStamp(); return true;
    }

    public void CancelUiInput() { Control(); CancelUiInputCore(); }

    public void SubmitUiActions(GameplayUiStamp expected, ReadOnlySpan<GameplayUiAction> actions)
    {
        Control();
        if (State != PlayState.Running || _uiAdmission == Guid.Empty || expected != UiStamp())
            throw new InvalidOperationException("Stale UI observation or inactive binding.");
        if (actions.Length > MaxUiActions - _pendingUiInput.Count ||
            (_pendingUiInput.Count != 0 && _pendingUiInput.Stamp != expected))
            throw new InvalidOperationException("UI queue is full or requires cancellation after a changed observation.");
        ulong sequence = _uiSequence;
        int textLength = 0;
        for (int i = 0; i < _pendingUiInput.Count; i++) textLength += _pendingUiInput[i].Text.Length;
        foreach (var action in actions) {
            ValidateUiActionName(action.Action);
            if (action.Sequence <= sequence || action.ElementId == Guid.Empty ||
                !_uiActions.TryGetValue(action.Action, out var registered) || registered != action.Kind ||
                !float.IsFinite(action.Value) || action.Value < 0 || action.Value > 1 ||
                (action.Kind != GameplayUiActionKind.ValueChanged && action.Value != 0))
                throw new ArgumentException("Invalid UI action identity, route, kind, value or sequence.");
            ValidateUiText(action.Text);
            if (action.Kind != GameplayUiActionKind.TextChanged && action.Text.Length != 0)
                throw new ArgumentException("Only text actions carry text.");
            textLength += action.Text.Length;
            if (textLength > MaxUiBatchTextLength) throw new ArgumentException("UI text queue exceeds its bound.");
            sequence = action.Sequence;
        }
        if (actions.IsEmpty) return;
        // Only publish after validating the entire batch. Caller arrays are never retained.
        var owned = new GameplayUiAction[_pendingUiInput.Count + actions.Length];
        for (int i = 0; i < _pendingUiInput.Count; i++) owned[i] = _pendingUiInput[i];
        actions.CopyTo(owned.AsSpan(_pendingUiInput.Count));
        _pendingUiInput = new(expected, owned); _uiSequence = sequence;
    }

    private GameplayUiStamp UiStamp() => new(SessionId, _document.World.Identity, _document.Revision, Tick,
        _uiInstance, _uiGeneration, _uiAdmission);

    private GameplayUiInput PrepareUiStep()
    {
        if (_pendingUiInput.Count != 0 && _pendingUiInput.Stamp != UiStamp()) CancelUiInputCore();
        return _pendingUiInput;
    }

    private void CancelUiInputCore()
    {
        _pendingUiInput = GameplayUiInput.Empty; _stepUiInput = null;
        _uiInstance = _uiAdmission = Guid.Empty; _uiGeneration = _uiSequence = 0;
    }

    private static void ValidateUiActionName(string action)
    {
        if (string.IsNullOrEmpty(action) || action.Length > 96 ||
            action.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-')))
            throw new ArgumentException("UI action must be a bounded exact identifier.");
    }

    private static void ValidateUiText(string text)
    {
        if (text is null || text.Length > MaxUiTextLength) throw new ArgumentException("UI text exceeds its bound.");
        for (int i = 0; i < text.Length; i++) {
            char c = text[i];
            if (c == '\0' || char.IsLowSurrogate(c)) throw new ArgumentException("UI text must be valid Unicode without NUL.");
            if (char.IsHighSurrogate(c) && (++i == text.Length || !char.IsLowSurrogate(text[i])))
                throw new ArgumentException("UI text must be valid Unicode.");
        }
    }
}
