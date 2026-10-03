#pragma once
#include <cstddef>
#include <cstdint>

// Managed NativeEntry mirrors this POD layout. All functions use cdecl and caller-owned buffers.
inline constexpr std::uint32_t NcmaGameplayBridgeVersion = 5;
struct NcmaExportPropertyV2
{
    std::uint32_t Kind = 0;
    std::uint32_t Reserved = 0;
    double DefaultValue = 0;
    char Name[128]{};
    char DisplayName[128]{};
    char Category[128]{};
};
static_assert(sizeof(NcmaExportPropertyV2) == 400);
static_assert(offsetof(NcmaExportPropertyV2, DefaultValue) == 8);

enum class NcmaPlayState : std::uint32_t { Stopped, Starting, Running, Paused, Faulted, Stopping };
// Read-only copied state; no pointers, ownership or native scheduling. Mirrors NativeEntry.PlayStatusV5.
struct NcmaPlayStatusV5
{
    std::uint32_t Version = 5;
    NcmaPlayState State = NcmaPlayState::Stopped;
    std::uint64_t SessionHigh = 0, SessionLow = 0, WorldHigh = 0, WorldLow = 0, FrameCount = 0, Tick = 0;
    std::int32_t StepsExecuted = 0;
    std::uint32_t Reserved = 0;
    double FixedDeltaSeconds = 0, SimulationSeconds = 0, Accumulator = 0, InterpolationAlpha = 0;
    double DroppedSeconds = 0, TotalDroppedSeconds = 0;
    std::uint32_t FaultCode = 0, Reserved2 = 0;
};
static_assert(sizeof(NcmaPlayStatusV5) == 120);
static_assert(offsetof(NcmaPlayStatusV5, SessionHigh) == 8);
static_assert(offsetof(NcmaPlayStatusV5, StepsExecuted) == 56);
static_assert(offsetof(NcmaPlayStatusV5, FixedDeltaSeconds) == 64);
static_assert(offsetof(NcmaPlayStatusV5, FaultCode) == 112);

struct NcmaInputFrameV1
{
    std::uint32_t Version = 1, Focused = 0;
    std::uint64_t Sequence = 0, SessionHigh = 0, SessionLow = 0;
    std::uint64_t Held[8]{}, Pressed[8]{}, Released[8]{};
    double PointerX = 0, PointerY = 0;
};
struct NcmaRenderObjectV1
{
    std::uint64_t ObjectHigh = 0, ObjectLow = 0;
    float Position[3]{}, Rotation[4]{}, Scale[3]{};
};
struct NcmaRenderHeaderV1
{
    std::uint32_t Version = 1, Count = 0;
    std::uint64_t SessionHigh = 0, SessionLow = 0, Tick = 0, FrameSequence = 0;
    double Alpha = 0;
};
static_assert(sizeof(NcmaInputFrameV1) == 240);
static_assert(offsetof(NcmaInputFrameV1, PointerX) == 224);
static_assert(sizeof(NcmaRenderObjectV1) == 56);
static_assert(sizeof(NcmaRenderHeaderV1) == 48);
