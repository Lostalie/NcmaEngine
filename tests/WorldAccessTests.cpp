#include "scene/SceneWorld.h"
#include "interop/NcmaNativeApi.h"

#include <array>
#include <cassert>
#include <limits>
#include <thread>
#include <vector>

namespace
{
    template<typename F> void Reject(F&& action)
    {
        bool rejected = false;
        try { action(); } catch (const std::exception&) { rejected = true; }
        assert(rejected);
    }
}

void TestWorldAccess()
{
    using namespace NcmaEngine;
    SceneWorld world("Exchange");
    const auto csharp = world.CreateObject("C#");
    const auto peer = world.CreateObject("Peer");
    const auto cs = world.GetReference(csharp), peerRef = world.GetReference(peer);
    const auto uuid = world.GetPersistentId(peer);
    std::array refs{cs, peerRef};
    std::array<Transform, 2> output;
    Transform first; first.Position.x() = 10;
    Transform second; second.Position.x() = 20;
    std::array writes{TransformWrite{cs, first}, TransformWrite{peerRef, second}};

    world.BeginGameplayPhase();
    world.WriteTransforms(writes);
    world.ReadTransforms(refs, output);
    assert(output[0].Position.x() == 0 && output[1].Position.x() == 0);
    assert(world.ReadGameplayTransform(peer).Position.x() == 0);
    world.SendSignal({cs, peerRef, 7, 12});
    std::array<GameplaySignal, 2> signals;
    assert(world.ReceiveSignals(peerRef, signals) == 0);
    Reject([&] { world.BeginGameplayPhase(); });
    Reject([&] { (void)world.CreateObject("During phase"); });
    Reject([&] { (void)world.DestroyObject(csharp); });
    std::string error;
    assert(!world.RestoreSnapshot(world.CaptureSnapshot(), error));
    world.CommitGameplayPhase();
    world.ReadTransforms(refs, output);
    assert(output[0].Position.x() == 10 && output[1].Position.x() == 20);
    assert(world.ReceiveSignals(peerRef, signals) == 1);
    assert(signals[0].Source == cs && signals[0].Code == 7 && signals[0].Value == 12 && signals[0].Sequence == 1);
    assert(world.ReceiveSignals(peerRef, signals) == 0);

    world.BeginGameplayPhase();
    world.WriteGameplayTransform(csharp, second);
    world.WriteGameplayTransform(csharp, first); // Last submission wins, no second queue slot.
    world.SendSignal({peerRef, cs, 8, 1});
    Reject([&] { world.SendSignal({peerRef, cs, 0, 1}); });
    Reject([&] { world.SendSignal({peerRef, cs, 1, std::numeric_limits<double>::infinity()}); });
    Reject([&] { world.SendSignal({peerRef, cs, 1, 0, 123}); });
    world.AbortGameplayPhase();
    assert(world.ReadGameplayTransform(csharp).Position.x() == 10);
    assert(world.ReceiveSignals(cs, signals) == 0);
    Reject([&] { world.SendSignal({cs, peerRef, 7, 12}); });
    Reject([&] { world.CommitGameplayPhase(); });
    writes[1].Value.Position.x() = std::numeric_limits<float>::quiet_NaN();
    Reject([&] { world.WriteTransforms(writes); });
    assert(world.ReadGameplayTransform(csharp).Position.x() == 10);
    writes[1] = writes[0];
    Reject([&] { world.WriteTransforms(writes); });

    SceneWorld other;
    const auto foreign = other.GetReference(other.CreateObject("Foreign"));
    output[0].Position.x() = 999;
    refs[1] = foreign;
    Reject([&] { world.ReadTransforms(refs, output); });
    assert(output[0].Position.x() == 999);
    bool threadRejected = false;
    std::thread worker([&] { try { (void)world.GetReference(csharp); }
        catch (const std::logic_error&) { threadRejected = true; } });
    worker.join();
    assert(threadRejected);

    // C ABI accepts a borrowed C++ World with exactly the same phase/validation rules.
    assert(ncma_get_world_access_api_version() == 1);
    NcmaObjectReference nativeCs{}, nativePeer{};
    assert(ncma_world_get_object_reference(&world, csharp, &nativeCs));
    assert(ncma_world_find_object(&world, uuid.High, uuid.Low, &nativePeer));
    std::array nativeRefs{nativeCs, nativePeer};
    std::array<NcmaTransform, 2> nativeOutput{};
    assert(ncma_world_read_transforms(&world, nativeRefs.data(), nativeOutput.data(), 2));
    assert(nativeOutput[1].Position.X == 20);
    auto nativeValue = nativeOutput[0]; nativeValue.Position.X = 30;
    assert(ncma_world_begin_gameplay_phase(&world));
    assert(ncma_world_set_local_transform(&world, csharp, &nativeValue));
    NcmaGameplaySignal nativeSignal{nativeCs, nativePeer, 19, 2.5, 0};
    assert(ncma_world_send_signal(&world, &nativeSignal));
    assert(ncma_world_get_local_transform(&world, csharp, &nativeOutput[0]));
    assert(nativeOutput[0].Position.X == 10); // Even legacy calls use phase semantics.
    assert(ncma_world_commit_gameplay_phase(&world));
    assert(world.GetLocalTransform(csharp).Position.x() == 30);
    std::array<NcmaGameplaySignal, 2> nativeSignals{};
    std::uint32_t received = 99;
    assert(!ncma_world_receive_signals(&world, nativePeer, nativeSignals.data(), 2, nullptr));
    assert(ncma_world_receive_signals(&world, nativePeer, nativeSignals.data(), 2, &received));
    assert(received == 1 && nativeSignals[0].Code == 19 && nativeSignals[0].Value == 2.5);
    assert(ncma_world_read_transforms(&world, nullptr, nullptr, 0));
    assert(!ncma_world_read_transforms(&world, nullptr, nullptr, 1));
    assert(!ncma_world_read_transforms(&world, nativeRefs.data(), nativeOutput.data(), 4097));
    const auto ownedForeign = ncma_world_create("DLL-owned world");
    assert(ncma_get_game_object_api_version() == 4);
    assert(ncma_world_create_object_with_language(ownedForeign, "Removed Python", 1) == 0);
    assert(ncma_world_create_object_with_language(ownedForeign, "Invalid", 99) == 0);
    const auto foreignId = ncma_world_create_object_with_language(ownedForeign, "Other", 0);
    NcmaObjectReference foreignNative{};
    assert(ncma_world_get_object_reference(ownedForeign, foreignId, &foreignNative));
    assert(!ncma_world_read_transforms(&world, &foreignNative, nativeOutput.data(), 1));
    ncma_world_destroy(ownedForeign);

    const auto snapshot = world.CaptureSnapshot();
    assert(world.RestoreSnapshot(snapshot, error));
    assert(world.FindObject(uuid) != peer && !world.Contains(peer));
    Reject([&] { (void)world.Resolve(peerRef); });
    assert(!ncma_world_read_transforms(&world, &nativePeer, nativeOutput.data(), 1));
    const auto restored = world.FindObject(uuid);
    const auto restoredRef = world.GetReference(restored);
    assert(world.DestroyObject(restored));
    assert(world.FindObject(uuid) == InvalidGameObjectId);
    Reject([&] { (void)world.Resolve(restoredRef); });

    SceneWorld bounded;
    const auto a = bounded.GetReference(bounded.CreateObject("A"));
    const auto b = bounded.GetReference(bounded.CreateObject("B"));
    bounded.BeginGameplayPhase();
    for (std::size_t i = 0; i < SceneWorld::AccessCapacity; ++i) bounded.SendSignal({a, b, 1, 0});
    Reject([&] { bounded.SendSignal({a, b, 1, 0}); });
    bounded.CommitGameplayPhase();
    assert(bounded.ReceiveSignals(a, signals) == 0);
    assert(bounded.ReceiveSignals(b, std::span<GameplaySignal>{}) == 0);
    assert(bounded.ReceiveSignals(b, signals) == 2);
    assert(signals[0].Sequence == 1 && signals[1].Sequence == 2);
    assert(bounded.DestroyObject(b.Id)); // Removes undeliverable signals, no silent queue leaks.

    // Full write capacity, no partial staging on overflow, and repeated-frame buffer reuse.
    SceneWorld many;
    std::vector<TransformWrite> fullWrites;
    fullWrites.reserve(SceneWorld::AccessCapacity);
    for (std::size_t i = 0; i < SceneWorld::AccessCapacity; ++i)
    {
        const auto id = many.CreateObject("Batch");
        fullWrites.push_back({many.GetReference(id), first});
    }
    const auto extra = many.CreateObject("Overflow");
    for (int frame = 0; frame < 2; ++frame)
    {
        many.BeginGameplayPhase();
        many.WriteTransforms(fullWrites);
        Reject([&] { many.WriteGameplayTransform(extra, second); });
        many.WriteGameplayTransform(fullWrites[0].Object.Id, second);
        many.CommitGameplayPhase();
        assert(many.ReadGameplayTransform(extra).Position.x() == 0);
        assert(many.ReadGameplayTransform(fullWrites[0].Object.Id).Position.x() == 20);
        assert(many.ReadGameplayTransform(fullWrites.back().Object.Id).Position.x() == 10);
    }
}
