namespace Ncma.Runtime;

public sealed partial class World
{
    // Trusted host-only proof, never an ambient privilege, component, serialized value or SDK API.
    private readonly Dictionary<(Guid Object, Type Component), ComponentAuthority> _authorities = [];
    private readonly Dictionary<Guid, int> _authorityObjects = [];
    private readonly HashSet<(Guid Object, Type Component)> _authorityWrites = [];

    internal sealed class ComponentAuthority(World world, Guid identity, Type component, Guid[] objects) : IDisposable
    {
        internal readonly World World = world;
        internal readonly Guid Identity = identity;
        internal readonly Type Component = component;
        internal readonly Guid[] Objects = objects;
        internal bool Released;

        public void Dispose()
        {
            World.VerifyStructuralAccess();
            if (Released) return;
            foreach (Guid uuid in Objects)
            {
                World._authorities.Remove((uuid, Component));
                if (--World._authorityObjects[uuid] == 0) World._authorityObjects.Remove(uuid);
            }
            Released = true;
        }
    }

    internal ComponentAuthority ClaimComponents<T>(ReadOnlySpan<Guid> objects) where T : struct, IComponent
    {
        VerifyStructuralAccess();
        _ = Components.Describe(typeof(T));
        if (objects.IsEmpty || objects.Length > MaxObjects) throw new ArgumentException("Invalid authority target budget.");
        Guid[] owned = objects.ToArray();
        var unique = new HashSet<Guid>();
        foreach (Guid uuid in owned)
            if (!unique.Add(uuid) || !FindObject(uuid).Has<T>() || _authorities.ContainsKey((uuid, typeof(T))))
                throw new ArgumentException("Duplicate, missing or already owned component authority.");
        var authority = new ComponentAuthority(this, _identity, typeof(T), owned);
        foreach (Guid uuid in owned)
        {
            _authorities.Add((uuid, typeof(T)), authority);
            _authorityObjects[uuid] = _authorityObjects.GetValueOrDefault(uuid) + 1;
        }
        return authority;
    }

    internal T WriteOwned<T>(ComponentAuthority authority, Guid uuid, T value) where T : struct, IComponent
    {
        VerifyAccess();
        try
        {
            if (!_updating || _initializing || authority.World != this || authority.Identity != _identity ||
                authority.Released || authority.Component != typeof(T) ||
                !_authorities.TryGetValue((uuid, typeof(T)), out var owner) || owner != authority)
                throw new InvalidOperationException("Expired or foreign component write authority.");
            SetCore(FindObject(uuid).Reference, value, authority);
            _authorityWrites.Add((uuid, typeof(T)));
            return (T)StepEntry(uuid).Entry.Components[typeof(T)];
        }
        catch (Exception error) { RejectStep(error); throw; }
    }

    // Batch callers preflight ALL targets before publishing any write, including outside a fixed step.
    internal void VerifyUnownedComponentWrite(ObjectReference reference, Type component)
    {
        VerifyComponentAuthority(Require(reference).PersistentId, component, null);
    }

    private void VerifyComponentAuthority(Guid uuid, Type component, ComponentAuthority? proof)
    {
        VerifyAccess();
        if (_authorities.TryGetValue((uuid, component), out var owner) && owner != proof)
        {
            var error = new InvalidOperationException("Component is reserved by its host write authority.");
            RejectStep(error); throw error;
        }
    }

    private void VerifyObjectStructure(Guid uuid)
    {
        VerifyAccess();
        if (_authorityObjects.ContainsKey(uuid))
        {
            var error = new InvalidOperationException("Bound object structure is frozen until its authority is released.");
            RejectStep(error); throw error;
        }
    }

    private void VerifyNoAuthorities()
    {
        if (_authorities.Count != 0) throw new InvalidOperationException("Release component authorities before restoring a World.");
    }

    private void VerifyAuthorityWrites()
    {
        if (!_initializing && _authorityWrites.Count != _authorities.Count)
            throw new InvalidOperationException("Every reserved component must be published by its owner before a fixed-step commit.");
    }
}
