using Ncma.Runtime;

namespace Ncma.Scene;

// Read-only consumer shape over OWNED cold snapshots. No revision, World, editing history,
// serialization dispatch or write capability. Authoring and runtime keep distinct formats.
public interface ISceneContent
{
    string Name { get; }
    IReadOnlyList<ISceneObjectContent> Objects { get; }
}
public interface ISceneObjectContent
{
    Guid Id { get; }
    string Name { get; }
    ComponentSnapshot[] Components { get; }
    BehaviourBindingData[] Behaviours { get; }
}
