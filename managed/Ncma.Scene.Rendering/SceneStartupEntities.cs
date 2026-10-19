using Ncma.Runtime;

namespace Ncma.Scene.Rendering;

// Off-frame composition only. These are owned membership copies, not live resource owners
// or per-frame query caches. Optional types must retain Has<T>'s absent-pool semantics.
internal static class SceneStartupEntities
{
    internal static Entity[] With<T>(World world) where T : struct, IComponent
    {
        world.VerifyAccess();
        return world.Components.IsRegistered<T>() ? Copy(world.Query<T>()) : [];
    }

    internal static Entity[] WithBoth<T1, T2>(World world)
        where T1 : struct, IComponent where T2 : struct, IComponent
    {
        world.VerifyAccess();
        return world.Components.IsRegistered<T1>() && world.Components.IsRegistered<T2>()
            ? Copy(world.Query<T1, T2>()) : [];
    }

    internal static Entity[] Without<T, TExcluded>(World world)
        where T : struct, IComponent where TExcluded : struct, IComponent
    {
        world.VerifyAccess();
        if (!world.Components.IsRegistered<T>()) return [];
        return Copy(world.CreateQuery([typeof(T)], world.Components.IsRegistered<TExcluded>() ? [typeof(TExcluded)] : []));
    }

    internal static Entity[] WithByUuid<T>(World world) where T : struct, IComponent
    {
        var result = With<T>(world);
        Array.Sort(result, (a, b) => world.GetUuid(a).CompareTo(world.GetUuid(b)));
        return result;
    }

    internal static Entity[] EitherByUuid<T1, T2>(World world)
        where T1 : struct, IComponent where T2 : struct, IComponent
    {
        world.VerifyAccess();
        var result = new List<Entity>();
        if (world.Components.IsRegistered<T1>())
            foreach (var entity in world.Query<T1>()) result.Add(entity);
        if (world.Components.IsRegistered<T2>())
            foreach (var entity in world.Query<T2>()) if (!world.Has<T1>(entity)) result.Add(entity);
        result.Sort((a, b) => world.GetUuid(a).CompareTo(world.GetUuid(b)));
        return result.ToArray(); // Union contains each committed entity once, at most World.MaxObjects.
    }

    private static Entity[] Copy(EntityQuery query)
    {
        var result = new Entity[query.Count];
        query.CopyTo(result);
        return result;
    }
}
