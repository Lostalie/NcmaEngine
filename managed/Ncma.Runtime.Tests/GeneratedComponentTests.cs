using System.Numerics;
using Ncma.Runtime;

internal static class GeneratedComponentTests
{
    private static void Check(bool value) { if (!value) throw new Exception("Generated component assertion"); }
    private static void Reject(Action action)
    { try { action(); } catch (Exception e) when (e is ArgumentException or InvalidOperationException) { return; } throw new Exception("Guard bypassed"); }
    internal static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("E6-B2 Transform metadata has explicit nested identities defaults and no automatic wire expansion", () => {
            var descriptor = global::Ncma.Runtime.CompositionRegistry.Build().Describe().Single();
            var metadata = descriptor.Metadata!;
            Check(metadata is not null && metadata.Fields.Count == 13);
            Check(metadata!.Fields.Select(f => f.FieldId).SequenceEqual(metadata.Fields.Select(f => f.FieldId).Order(StringComparer.Ordinal)));
            Check(metadata.Fields.Single(f => f.FieldId == "rotation.w") is { MemberPath: "Rotation.W", JsonPath: "rotation.w", ValueKind: "number" } field && field.DefaultValue.GetSingle() == 1);
            Check(metadata.Fields.Single(f => f.FieldId == "scale.x").DefaultValue.GetSingle() == 1);
            Check(metadata.DefaultValue.GetProperty("position").GetProperty("x").GetSingle() == 0);
            Check(metadata.Fields.All(f => f.Flags == TransformData.FieldUsage));
            Check(!System.Text.Json.JsonSerializer.SerializeToElement(descriptor).TryGetProperty("Metadata", out _));
            bool immutable = false;
            try { ((IList<ComponentFieldDescriptor>)metadata.Fields)[0] = metadata.Fields[0] with { FieldId = "changed" }; }
            catch (NotSupportedException) { immutable = true; }
            Check(immutable && !metadata.Fields.Any(f => f.FieldId == "changed"));
        });
        yield return ("E6-B2 metadata copies caller definitions before default callback and normalizes defaults", () => {
            var fields = TransformData.Generated.DescribeFields(); var registry = new ComponentRegistry();
            registry.Register("test.transform", 1, TransformData.DefinitionSchema, TransformData.Validate, fields: fields, defaultFactory: () => {
                fields[0] = fields[0] with { FieldId = "changed", JsonPath = "missing" };
                return TransformData.Identity with { Rotation = new(0, 0, 0, 2) };
            });
            var metadata = registry.Describe().Single().Metadata!;
            Check(!metadata.Fields.Any(f => f.FieldId == "changed") && metadata.Fields.Single(f => f.FieldId == "rotation.w").DefaultValue.GetSingle() == 1);
            Check(registry.Decode<TransformData>(new("test.transform", 1, metadata.DefaultValue)) == TransformData.Identity);
            var plain = new ComponentRegistry(); plain.Register<TransformData>("plain", 1, TransformData.DefinitionSchema, TransformData.Validate);
            Check(plain.Describe().Single().Metadata is null); // Honest missing declarations; not inferred field IDs.
        });
        yield return ("E6-B2 bad field IDs paths flags budgets schema and defaults never publish", () => {
            foreach (int variant in Enumerable.Range(0, 8)) {
                var registry = new ComponentRegistry(); var fields = TransformData.Generated.DescribeFields();
                switch (variant) {
                    case 0: fields[0] = fields[0] with { FieldId = fields[1].FieldId }; break;
                    case 1: fields[0] = fields[0] with { MemberPath = "Missing" }; break;
                    case 2: fields[0] = fields[0] with { Flags = ComponentFieldFlags.Persistent | ComponentFieldFlags.RuntimeWrite }; break;
                    case 3: fields = fields[..^1]; break;
                    case 4: fields = Enumerable.Repeat(fields[0], ComponentMetadata.MaxFields + 1).ToArray(); break;
                    case 5: fields[0] = fields[0] with { Flags = (ComponentFieldFlags)32 }; break;
                }
                string schema = variant == 6 ? TransformData.DefinitionSchema.Replace("\"number\"", "\"integer\"") : TransformData.DefinitionSchema;
                bool called = false;
                Reject(() => registry.Register("test.transform", 1, schema, TransformData.Validate, fields: fields,
                    defaultFactory: () => { called = true; return variant == 7 ? TransformData.Identity with { Position = new(float.NaN, 0, 0) } : TransformData.Identity; }));
                Check(called == (variant == 7));
                Check(!registry.IsRegistered<TransformData>() && registry.Describe().Count == 0);
                TransformData.Generated.Register(registry); Check(registry.Describe().Count == 1);
            }
        });
        yield return ("E6-B2 default callbacks cannot reenter or freeze registration and frozen registry skips callback", () => {
            foreach (bool freeze in new[] { false, true }) {
                var registry = new ComponentRegistry();
                Reject(() => registry.Register("test.transform", 1, TransformData.DefinitionSchema, TransformData.Validate,
                    fields: TransformData.Generated.DescribeFields(), defaultFactory: () => {
                        if (freeze) _ = new World(components: registry); else TransformData.Generated.Register(registry);
                        return TransformData.Identity;
                    }));
                Check(!registry.IsRegistered<TransformData>() && registry.Describe().Count == 0);
                TransformData.Generated.Register(registry); _ = new World(components: registry);
                bool called = false;
                Reject(() => registry.Register("other", 1, TransformData.DefinitionSchema, TransformData.Validate,
                    fields: TransformData.Generated.DescribeFields(), defaultFactory: () => { called = true; return TransformData.Identity; }));
                Check(!called && registry.Describe().Count == 1);
            }
        });
        yield return ("E6-B generated registration preserves identity default and registry guards", () => {
            var registry = new ComponentRegistry(); TransformData.Generated.Register(registry);
            var descriptor = registry.Describe().Single();
            Check(descriptor.TypeId == "ncma.transform" && descriptor.Version == 1);
            Check(TransformData.Generated.Default == TransformData.Identity);
            Check(descriptor.Schema.GetProperty("required").GetArrayLength() == 3);
            Reject(() => TransformData.Generated.Register(registry));
            _ = new World(components: registry);
            Reject(() => TransformData.Generated.Register(registry));
            var different = new ComponentRegistry();
            different.Register<TransformData>("other.transform", 1, TransformData.DefinitionSchema, TransformData.Validate);
            Reject(() => TransformData.Generated.ReadAccess(different));
        });
        yield return ("E6-B generated World access preserves validation identity thread and readonly guards", () => {
            var world = new World(); var entity = world.CreateEntity("generated");
            TransformData.Generated.Write(world, entity, TransformData.Identity with { Rotation = new(0, 0, 0, 2) });
            Check(TransformData.Generated.Read(world, entity) == TransformData.Identity);
            var query = TransformData.Generated.Query(world); Check(query.Count == 1);
            var revision = world.Revision;
            Reject(() => TransformData.Generated.Write(world, entity, TransformData.Identity with { Position = new(float.NaN, 0, 0) }));
            using (world.ReadOnly()) Reject(() => TransformData.Generated.Write(world, entity, TransformData.Identity));
            Check(world.Revision == revision);
            Reject(() => TransformData.Generated.Read(new World(), entity));
            Check(Task.Run(() => { try { TransformData.Generated.Read(world, entity); return false; } catch (InvalidOperationException) { return true; } }).GetAwaiter().GetResult());
            world.Destroy(entity); Check(query.Count == 0); Reject(() => TransformData.Generated.Read(world, entity));
        });
        yield return ("E6-B generated schedule access uses original checked contexts and phase contract", () => {
            var world = new World(); var entity = world.CreateEntity("scheduled"); world.Set(entity, TransformData.Identity);
            var access = TransformData.Generated.WriteAccess(world.Components);
            Check(access == ScheduleAccess.Write<TransformData>(world.Components));
            var task = new ScheduleTask("generated", 0, ScheduleAffinity.OwnerThread, [access], [], []);
            Reject(() => ScheduleGraph.Compile(world.Components, [task, task with { Id = "later", Phase = 1, Access = [TransformData.Generated.ReadAccess(world.Components)] }]));
            var runner = new WorldRunner(world); ScheduleContext? expired = null;
            using var executor = new ScheduleExecutor(world, ScheduleGraph.Compile(world.Components, [task]), [new("generated", c => {
                expired = c; var value = TransformData.Generated.ReadScheduled(c, 0);
                TransformData.Generated.WriteScheduled(c, 0, value with { Position = Vector3.UnitX });
            })], [TransformData.Generated.Channel()]);
            runner.ConfigureSchedule(executor);
            try { runner.Advance(1d / 60); Check(world.Get<TransformData>(entity).Position == Vector3.UnitX && world.Tick == 1); }
            finally { runner.ClearSchedule(); }
            Check(expired is not null); Reject(() => TransformData.Generated.ReadScheduled(expired!, 0));
        });
    }
}
