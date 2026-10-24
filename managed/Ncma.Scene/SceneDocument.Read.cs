using Ncma.Runtime;

namespace Ncma.Scene;

public sealed partial class SceneDocument
{
    private void VerifyReadBoundary()
    {
        VerifyAccess();
        if (_preparing || _reading) throw new InvalidOperationException("Authoring read requires a committed boundary.");
    }
    // Every published authoring value has already passed the whole-document byte/metadata check.
    // Page reads need no full-scene capture, live ECS query or callback-based serialization proof.
    internal SceneDocumentSnapshot ReadPage(int offset, int limit, out int totalCount)
    {
        VerifyReadBoundary();
        if (offset < 0 || limit is < 1 or > 128) throw new ArgumentException("Invalid pagination.");
        totalCount = _content.Objects.Length;
        return SceneDocumentCodec.Copy(_content with { Objects = _content.Objects.Skip(offset).Take(limit).ToArray() });
    }
    internal SceneObjectData? ReadObject(Guid id)
    {
        VerifyReadBoundary();
        return !_index.TryGetValue(id, out int index) ? null :
            SceneDocumentCodec.Copy(_content with { Objects = [_content.Objects[index]] }).Objects[0];
    }
    public ComponentSnapshot[] GetComponents(Guid id)
    {
        VerifyReadBoundary();
        return RequireObject(id).Components.Select(c => c with { Data = c.Data.Clone() }).ToArray();
    }
    public bool HasComponent<T>(Guid id) where T : struct, IComponent
    {
        VerifyReadBoundary(); string type = Components.Describe(typeof(T)).TypeId;
        return RequireObject(id).Components.Any(c => c.TypeId == type);
    }
    public T GetComponent<T>(Guid id) where T : struct, IComponent
    {
        VerifyReadBoundary(); string type = Components.Describe(typeof(T)).TypeId;
        var component = RequireObject(id).Components.SingleOrDefault(c => c.TypeId == type)
            ?? throw new ArgumentException("Authoring component is absent.");
        // Decode a detached value, never store mutable custom structs beside the authoring JSON.
        _reading = true;
        try { using var read = ReadOnly(); return Components.Decode<T>(component with { Data = component.Data.Clone() }); }
        finally { _reading = false; }
    }
    public void SetComponent<T>(Guid id, T value) where T : struct, IComponent => Change(_ =>
    {
        var descriptor = Components.Describe(typeof(T));
        var component = new ComponentSnapshot(descriptor.TypeId, descriptor.Version, Components.Encode(value));
        Components.ValidateEncoding(component); return ReplaceComponent(id, component);
    });
    public void SetComponent(Guid id, ComponentSnapshot component) => Change(_ =>
    {
        var normalized = NormalizeComponents([component]); return ReplaceComponent(id, normalized[0]);
    });
    private SceneDocumentSnapshot ReplaceComponent(Guid id, ComponentSnapshot component)
    {
        var item = RequireObject(id);
        return Replace(item with { Components = item.Components.Where(c => c.TypeId != component.TypeId)
            .Append(component).OrderBy(c => c.TypeId, StringComparer.Ordinal).ToArray() });
    }
    public void RemoveComponent<T>(Guid id) where T : struct, IComponent => RemoveComponent(id, Components.Describe(typeof(T)).TypeId);
    public void RemoveComponent(Guid id, string typeId) => Change(_ =>
    {
        var item = RequireObject(id);
        if (!item.Components.Any(c => c.TypeId == typeId)) throw new ArgumentException("Authoring component is absent.");
        return Replace(item with { Components = item.Components.Where(c => c.TypeId != typeId).ToArray() });
    });
}
