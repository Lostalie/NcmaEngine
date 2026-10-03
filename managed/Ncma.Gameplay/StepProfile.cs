namespace Ncma.Gameplay;

// Trusted diagnostics only. No callbacks, injectable code, protocol registration or live-model exposure.
internal readonly record struct StepProfile(long Setup, long Callbacks, long InputPrepare, long WorldSignalsPrepare,
    long MetadataPrepare, long InstancesPrepare, long ReceiptsPrepare, long RenderPrepare, long Install);
