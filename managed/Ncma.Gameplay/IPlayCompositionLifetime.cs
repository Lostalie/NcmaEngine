namespace Ncma.Gameplay;

// Trusted host ownership only. Not a gameplay SDK, serialized component or Agent capability.
internal interface IPlayCompositionLifetime { void Start(); void Stop(); }
