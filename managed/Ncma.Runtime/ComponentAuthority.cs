namespace Ncma.Runtime;

public sealed partial class World
{
    // Trusted host-only proof, never an ambient privilege, component, serialized value or SDK API.
    private readonly Dictionary<(Guid Object, Type Component), ComponentAuthority> _authorities = [];
    private readonly Dictionary<Guid, int> _authorityObjects = [];
    private readonly HashSet<(Guid Object, Type Component)> _authorityWrites = [];
    private readonly Dictionary<Type, ComponentAuthority> _frozenComponentTypes = [];
    private int _requiredAuthorityWrites;

    internal sealed class ComponentAuthority(World world, Guid identity, Type component, Guid[] objects, bool publishRequired, bool freezeMembership) : IDisposable
    {
        internal readonly World World = world;
        internal readonly Guid Identity = identity;
        internal readonly Type Component = component;
        internal readonly Guid[] Objects = objects;
        internal readonly bool PublishRequired = publishRequired;
        internal readonly bool FreezeMembership = freezeMembership;
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
            if (PublishRequired) World._requiredAuthorityWrites -= Objects.Length;
            if (FreezeMembership) World._frozenComponentTypes.Remove(Component);
            Released = true;
        }
    }

    internal ComponentAuthority ClaimComponents<T>(ReadOnlySpan<Guid> objects, bool publishRequired = true, bool freezeMembership = false) where T : struct, IComponent
    {
        VerifyStructuralAccess();
        _ = Components.Describe(typeof(T));
        if (objects.IsEmpty && !freezeMembership || objects.Length > MaxObjects || freezeMembership && publishRequired || _frozenComponentTypes.ContainsKey(typeof(T)))
            throw new ArgumentException("Invalid authority target budget or frozen component type.");
        Guid[] owned = objects.ToArray();
        var unique = new HashSet<Guid>();
        foreach (Guid uuid in owned)
            if (!unique.Add(uuid) || !FindObject(uuid).Has<T>() || _authorities.ContainsKey((uuid, typeof(T))))
                throw new ArgumentException("Duplicate, missing or already owned component authority.");
        if (freezeMembership && GetObjects().Count(o => o.Has<T>()) != owned.Length)
            throw new ArgumentException("Frozen component membership must cover every existing target.");
        var authority = new ComponentAuthority(this, _identity, typeof(T), owned, publishRequired, freezeMembership);
        foreach (Guid uuid in owned)
        {
            _authorities.Add((uuid, typeof(T)), authority);
            _authorityObjects[uuid] = _authorityObjects.GetValueOrDefault(uuid) + 1;
        }
        if (publishRequired) _requiredAuthorityWrites += owned.Length;
        if (freezeMembership) _frozenComponentTypes.Add(typeof(T), authority);
        return authority;
    }

    internal T WriteOwned<T>(ComponentAuthority authority, Guid uuid, T value) where T : struct, IComponent
    {
        VerifyAccess();
        try
        {
            if (!_updating || _initializing || authority.World != this || authority.Identity != _identity ||
                authority.Released || !authority.PublishRequired || authority.Component != typeof(T) ||
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

    private void VerifyComponentTopology(Type component)
    {
        VerifyAccess();
        if (_frozenComponentTypes.ContainsKey(component)) {
            var error = new InvalidOperationException("Component membership is frozen by its host until the authority is released.");
            RejectStep(error); throw error;
        }
    }

    private void VerifyNoAuthorities()
    {
        if (_authorities.Count != 0 || _frozenComponentTypes.Count != 0) throw new InvalidOperationException("Release component authorities before restoring a World.");
    }

    private void VerifyAuthorityWrites()
    {
        if (!_initializing && _authorityWrites.Count != _requiredAuthorityWrites)
            throw new InvalidOperationException("Every reserved component must be published by its owner before a fixed-step commit.");
    }
}
