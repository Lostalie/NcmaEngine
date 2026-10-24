namespace Ncma.Player.App;

// Requested client size, not a promise about physical pixels after OS/DPI adjustment.
public readonly record struct PlayerWindowSize(uint Width, uint Height)
{
    public static PlayerWindowSize Default => new(1280, 720);
    public void Validate()
    {
        if (Width is 0 or > 8192 || Height is 0 or > 8192)
            throw new ArgumentException("invalid_window_size");
    }
}

public sealed record PlayerOutputReport(PlayerWindowSize RequestedWindowSize,
    PlayerWindowSize InitialFramebufferSize, PlayerWindowSize? LastPresentedSize,
    ulong SuccessfulPresents, ulong SizeTransitions, ulong PresentsMatchingRequestedWindow)
{
    public int Version => 1;
    public Ncma.Platform.WindowMode RequestedMode { get; init; } = Ncma.Platform.WindowMode.Windowed;
    public bool AllPresentSizesMatchRequested => SuccessfulPresents != 0 &&
        PresentsMatchingRequestedWindow == SuccessfulPresents;
    public bool TargetPerformanceAccepted => false;
    public string Scope => "requested client size versus observed render framebuffer after successful Present; not physical display or internal render scale";
}

// Owner is PlayerPresentation. No per-frame arrays, reports, native queries or observer opt-in.
internal sealed class PlayerOutputObservation(PlayerWindowSize requested, PlayerWindowSize initial,
    Ncma.Platform.WindowMode mode=Ncma.Platform.WindowMode.Windowed)
{
    private PlayerWindowSize? _last;
    private ulong _presents, _transitions, _matching;
    public void Presented(uint width, uint height)
    {
        var size = new PlayerWindowSize(width, height);
        size.Validate();
        if (_last is { } previous && previous != size) _transitions++;
        if (size == requested) _matching++;
        _last = size;
        _presents++;
    }
    public PlayerOutputReport Capture() => new(requested, initial, _last, _presents, _transitions, _matching){RequestedMode=mode};
}
