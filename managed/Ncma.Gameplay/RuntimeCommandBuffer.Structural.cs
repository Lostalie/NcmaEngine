using System.Text;
using Ncma.Runtime;
using Ncma.Scene;

namespace Ncma.Gameplay;

internal sealed partial class RuntimeCommandBuffer
{
    public StructuralSubmission SubmitBatch(ReadOnlySpan<StructuralCommand> commands)
    {
        session.Scene.VerifyAccess();
        bool entered = false;
        try
        {
            session.Scene.World.VerifyWriteAccess();
            if (!_active || !session.Scene.World.IsUpdating || _submitting)
                throw new InvalidOperationException("Structural batches require a non-reentrant fixed-step callback.");
            if (commands.Length == 0 || commands.Length > Capacity - _pending.Count || (ulong)commands.Length > ulong.MaxValue - _sequence)
                throw new ArgumentException("Structural batch command budget exceeded.");
            _submitting = true; entered = true;
            var world = session.Scene.World;
            var copied = new StructuralCommand[commands.Length];
            var targets = new Guid[commands.Length];
            var componentBytes = new int[commands.Length];
            var tokens = new CommandToken[commands.Length];
            var handles = new PendingEntity?[commands.Length];
            var created = new HashSet<Guid>(_created);
            var touched = new HashSet<(Guid Object, string Field)>();
            var objects = new HashSet<Guid>();
            var destroyed = new HashSet<Guid>();
            int bytes = 0;
            for (int i = 0; i < commands.Length; ++i)
            {
                var command = commands[i] ?? throw new ArgumentException("Null structural command.");
                int size = 128;
                string field;
                Guid target;
                switch (command)
                {
                    case StructuralCommand.Create c:
                        World.ValidateName(c.Name);
                        if (c.ObjectId == Guid.Empty || !created.Add(c.ObjectId)) throw new ArgumentException("Duplicate creation reservation.");
                        target = c.ObjectId; field = "create"; size += Encoding.UTF8.GetByteCount(c.Name); break;
                    case StructuralCommand.Destroy c:
                        target = Target(c.Target, i); field = "destroy";
                        if (objects.Contains(target)) throw new ArgumentException("Destroy is exclusive within its batch.");
                        destroyed.Add(target); break;
                    case StructuralCommand.Rename c:
                        target = Target(c.Target, i); World.ValidateName(c.Name); field = "name"; size += Encoding.UTF8.GetByteCount(c.Name); break;
                    case StructuralCommand.Add c:
                        target = Target(c.Target, i); ArgumentNullException.ThrowIfNull(c.Component);
                        ValidateTypeId(c.Component.TypeId);
                        componentBytes[i] = SceneJson.EncodeBounded(c.Component, ComponentRegistry.MaxPayloadBytes + 512).Length;
                        command = c with { Component = c.Component with { Data = c.Component.Data.Clone() } };
                        field = "component:" + c.Component.TypeId; size += componentBytes[i]; break;
                    case StructuralCommand.Remove c:
                        target = Target(c.Target, i); ValidateTypeId(c.TypeId); field = "component:" + c.TypeId;
                        size += Encoding.UTF8.GetByteCount(c.TypeId); break;
                    case StructuralCommand.Attach c:
                        target = Target(c.Target, i); ArgumentNullException.ThrowIfNull(c.Binding);
                        if (string.IsNullOrWhiteSpace(c.Binding.TypeName) || c.Binding.TypeName.Length > 512 ||
                            c.Binding.Exports is null || c.Binding.Exports.Length > BehaviourBindingRules.MaxExportsPerBinding ||
                            c.Binding.Exports.Any(e => e is null || string.IsNullOrWhiteSpace(e.Name) || e.Name.Length > 128))
                            throw new ArgumentException("Invalid binding payload budget.");
                        size += SceneJson.EncodeBounded(c.Binding, PayloadBytes - _bytes - bytes).Length;
                        command = c with { Binding = c.Binding with { Exports = (RuntimeExport[])c.Binding.Exports.Clone() } };
                        field = "binding:" + c.Binding.Id; break;
                    case StructuralCommand.Detach c:
                        target = Target(c.Target, i); field = "binding:" + c.BindingId; break;
                    case StructuralCommand.Enable c:
                        target = Target(c.Target, i); field = "binding:" + c.BindingId; break;
                    default: throw new ArgumentException("Unknown structural command.");
                }
                if ((field != "destroy" && destroyed.Contains(target)) || !touched.Add((target, field)))
                    throw new ArgumentException("Duplicate or conflicting batch mutation target.");
                objects.Add(target);
                if (size > PayloadBytes - _bytes - bytes) throw new ArgumentException("Structural batch payload budget exceeded.");
                bytes += size; copied[i] = command; targets[i] = target;
            }
            // Burn token numbers even if later validation fails; never revive aborted submissions.
            for (int i = 0; i < tokens.Length; ++i) tokens[i] = new(session.SessionId, _generation, ++_sequence);
            bool changesBindings = copied.Any(c => c is StructuralCommand.Create or StructuralCommand.Destroy or
                StructuralCommand.Attach or StructuralCommand.Detach or StructuralCommand.Enable);
            var bindings = changesBindings ? new Dictionary<Guid, BehaviourBindingData[]>(_bindings) : _bindings;
            var pending = new List<(CommandToken Token, Guid Target)>(_pending);
            pending.EnsureCapacity(pending.Count + commands.Length);
            for (int i = 0; i < targets.Length; ++i) pending.Add((tokens[i], targets[i]));
            var submission = new StructuralSubmission(tokens, handles);
            Action install = world.PrepareStructuralBatch(() =>
            {
                for (int i = 0; i < copied.Length; ++i)
                {
                    Apply(copied[i], targets[i], bindings);
                    if (copied[i] is StructuralCommand.Create)
                        handles[i] = new PendingEntity(targets[i], tokens[i], _attempt, world.CandidateEntity(targets[i]));
                }
            }, snapshot =>
            {
                // Exact complete-document and normalized batch budgets share the checked candidate encoding.
                _ = session.Scene.PrepareRuntimeStep(snapshot, bindings);
                int normalizedBytes = bytes;
                for (int i = 0; i < copied.Length; ++i)
                    if (copied[i] is StructuralCommand.Add add)
                    {
                        int componentSize = snapshot.ComponentBytes(targets[i], add.Component.TypeId);
                        if (componentSize > ComponentRegistry.MaxPayloadBytes + 512) throw new ArgumentException("Normalized component budget exceeded.");
                        normalizedBytes = checked(normalizedBytes - componentBytes[i] + componentSize);
                        if (normalizedBytes > PayloadBytes - _bytes) throw new ArgumentException("Normalized batch payload budget exceeded.");
                    }
                // Charge the greater encoded size: normalization cannot grant a budget bypass.
                bytes = Math.Max(bytes, normalizedBytes);
            });
            install();
            _bindings = bindings; _created = created; _pending = pending; _bytes += bytes;
            if (changesBindings) _bindingsShared = false;
            return submission;

            Guid Target(StructuralTarget target, int operation)
            {
                switch (target.Kind)
                {
                    case 1: return world.GetUuid(world.Resolve(target.Entity));
                    case 2:
                        if (target.Operation < 0 || target.Operation >= operation || copied[target.Operation] is not StructuralCommand.Create)
                            throw new ArgumentException("Creation dependency must reference an earlier Create in this batch.");
                        return targets[target.Operation];
                    case 3:
                        var handle = target.Pending ?? throw new ArgumentException("Missing pending handle.");
                        if (handle.Attempt != _attempt || handle.Token.SessionId != session.SessionId || handle.Token.Generation != _generation ||
                            !_pending.Contains((handle.Token, handle.ObjectId)) || world.CandidateEntity(handle.ObjectId) != handle.Entity)
                            throw new ArgumentException("Stale, foreign, removed or aborted pending handle.");
                        return handle.ObjectId;
                    default: throw new ArgumentException("Invalid structural target.");
                }
            }
        }
        catch (Exception error) { session.Scene.World.RejectStep(error); throw; }
        finally { if (entered) _submitting = false; }
    }

    private static void ValidateTypeId(string typeId)
    {
        if (string.IsNullOrWhiteSpace(typeId) || typeId.Length > 128) throw new ArgumentException("Invalid component type identity.");
    }

    private void Apply(StructuralCommand command, Guid id, Dictionary<Guid, BehaviourBindingData[]> bindings)
    {
        var world = session.Scene.World;
        switch (command)
        {
            case StructuralCommand.Create c: world.StageCreate(id, c.Name); bindings.Add(id, []); break;
            case StructuralCommand.Destroy: world.StageDestroy(id); bindings.Remove(id); break;
            case StructuralCommand.Rename c: world.StageRename(id, c.Name); break;
            case StructuralCommand.Add c: world.StageAdd(id, c.Component); break;
            case StructuralCommand.Remove c: world.StageRemove(id, c.TypeId); break;
            case StructuralCommand.Attach c:
                world.RequireCommandTarget(id);
                if (bindings.Values.SelectMany(v => v).Any(b => b.Id == c.Binding.Id)) throw new ArgumentException("Duplicate binding UUID.");
                bindings[id] = [.. bindings.GetValueOrDefault(id, []), new(c.Binding.Id, c.Binding.TypeName, c.Binding.Enabled,
                    c.Binding.Exports.Select(e => e is null ? throw new ArgumentException("Null export.") : new ExportData(e.Name, (ExportKind)e.Kind, e.Value)).ToArray())];
                break;
            case StructuralCommand.Detach c:
                world.RequireCommandTarget(id);
                var old = bindings.GetValueOrDefault(id, []);
                if (!old.Any(b => b.Id == c.BindingId)) throw new ArgumentException("Unknown binding.");
                bindings[id] = old.Where(b => b.Id != c.BindingId).ToArray(); break;
            case StructuralCommand.Enable c:
                world.RequireCommandTarget(id);
                var existing = bindings.GetValueOrDefault(id, []);
                if (!existing.Any(b => b.Id == c.BindingId)) throw new ArgumentException("Unknown binding.");
                bindings[id] = existing.Select(b => b.Id == c.BindingId ? b with { Enabled = c.Enabled } : b).ToArray(); break;
            default: throw new ArgumentException("Unknown structural command.");
        }
    }
}
