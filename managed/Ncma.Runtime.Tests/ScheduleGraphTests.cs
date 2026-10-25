using Ncma.Runtime;

internal static class ScheduleGraphTests
{
    private static void Check(bool value) { if (!value) throw new Exception("Schedule graph assertion"); }
    private static void Reject(Action action)
    { try { action(); } catch (ArgumentException) { return; } throw new Exception("Invalid graph accepted"); }
    private static ScheduleTask Task(string id, params ScheduleAccess[] access) => new(id, 0, ScheduleAffinity.OwnerThread, access, [], []);
    internal static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("E6-C cross-phase component Read and read-capable Write reject with exact diagnostic", () => {
            var registry=global::Ncma.Runtime.CompositionRegistry.Build();var write=ScheduleAccess.Write<TransformData>(registry);
            foreach(var mode in new[]{ScheduleAccessMode.Read,ScheduleAccessMode.Write}) foreach(bool linked in new[]{false,true}) {
                var writer=Task("z.writer",write) with{Phase=2};
                var reader=Task("a.reader",write with{Mode=mode}) with{Phase=15,After=linked?["z.writer"]:[]};
                string? message=null;
                foreach(var tasks in new[]{new[]{writer,reader},new[]{reader,writer}}) {
                    ArgumentException? error=null;try{ScheduleGraph.Compile(registry,tasks);}catch(ArgumentException e){error=e;}
                    Check(error is not null && error.Message.StartsWith("cross_phase_component_read_requires_resource:",StringComparison.Ordinal));
                    Check(error!.Message.Contains(write.Id) && error.Message.Contains("z.writer@phase2") && error.Message.Contains("a.reader@phase15") && error.Message.Contains("Resource channel + ReadOutput"));
                    if(message is not null)Check(message==error.Message);message=error.Message;
                }
            }
        });
        yield return ("E6-C cross-phase guard leaves same-phase snapshots pure reads and resource namespace intact", () => {
            var registry=global::Ncma.Runtime.CompositionRegistry.Build();var read=ScheduleAccess.Read<TransformData>(registry);var write=read with{Mode=ScheduleAccessMode.Write};
            Check(ScheduleGraph.ContractVersion==3);
            Check(ScheduleGraph.Compile(registry,[Task("writer",write),Task("reader",read)]).TaskCount==2);
            Check(ScheduleGraph.Compile(registry,[Task("early",read),Task("late",read)with{Phase=15}]).TaskCount==2);
            Check(ScheduleGraph.Compile(registry,[Task("early",read),Task("late",write)with{Phase=15}]).TaskCount==2);
            // A resource with the same string ID is a distinct access namespace.
            Check(ScheduleGraph.Compile(registry,[Task("writer",write),Task("reader",ScheduleAccess.Resource(read.Id,ScheduleAccessMode.Read))with{Phase=15}],[new(read.Id)]).TaskCount==2);
        });
        yield return ("E6-C 128 tasks across 16 phases have a linear anchor chain, not pairwise phase edges", () => {
            var registry=global::Ncma.Runtime.CompositionRegistry.Build();
            var tasks=Enumerable.Range(0,128).Select(i=>Task("t"+i.ToString("D3")) with{Phase=i%16}).ToArray();
            var graph=ScheduleGraph.Compile(registry,tasks);
            Check(graph.TaskCount==128 && graph.PhaseAnchorCount==15 && graph.EdgeCount==254);
            Check(graph.CopyEdges().Count(e=>e.Reason=="phase-chain")==14 && graph.CopyEdges().All(e=>e.From.StartsWith('$')||e.To.StartsWith('$')));
            var byId=tasks.ToDictionary(t=>t.Id);var order=graph.CopySerialOrder();
            Check(order.Length==128 && order.All(byId.ContainsKey) && order.Select(id=>byId[id].Phase).SequenceEqual(Enumerable.Range(0,16).SelectMany(p=>Enumerable.Repeat(p,8))));
            Check(graph.ContentHash==ScheduleGraph.Compile(registry,tasks.Reverse()).ContentHash);
            var anchors=graph.CopyPhaseAnchors();anchors[0]=new("changed",4,5);Check(graph.CopyPhaseAnchors()[0]==new SchedulePhaseAnchor("$phase:0",0,1));
        });
        yield return ("E6-C cross-phase declarations and conflicts reuse anchors; reverse dependencies reject", () => {
            var registry=global::Ncma.Runtime.CompositionRegistry.Build();var write=ScheduleAccess.Resource("phase.output",ScheduleAccessMode.Write);
            var graph=ScheduleGraph.Compile(registry,[Task("a",write) with{Before=["b","c"]},Task("b",write) with{Phase=7},Task("c",write) with{Phase=7,After=["a"]}],[new("phase.output")]);
            Check(graph.PhaseAnchorCount==1 && graph.EdgeCount==4 && graph.CopyTasks()[0].Before.SequenceEqual(new[]{"b","c"}));
            Check(!graph.CopyEdges().Any(e=>e.From=="a" && (e.To=="b"||e.To=="c")) && graph.CopyPhaseAnchors()[0].NextPhase==7);
            Reject(()=>ScheduleGraph.Compile(registry,[Task("a") with{After=["b"]},Task("b") with{Phase=7}]));
            Reject(()=>ScheduleGraph.Compile(registry,[Task("a"),Task("b") with{Phase=7,Before=["a"]}]));
            Reject(()=>ScheduleGraph.Compile(registry,[Task("$phase:0")]));
            Reject(()=>ScheduleGraph.Compile(registry,[Task("a") with{Before=["$phase:0"]}]));
        });
        yield return ("E6-C dense same-phase conflicts retain real edges without anchor budget false rejection", () => {
            var registry=global::Ncma.Runtime.CompositionRegistry.Build();var write=ScheduleAccess.Write<TransformData>(registry);
            var tasks=Enumerable.Range(0,128).Select(i=>Task("t"+i.ToString("D3"),write)).ToArray();
            var single=ScheduleGraph.Compile(registry,tasks);Check(single.PhaseAnchorCount==0 && single.EdgeCount==8128);
            tasks[127]=tasks[127] with{Phase=15};Reject(()=>ScheduleGraph.Compile(registry,tasks));
            var resource=ScheduleAccess.Resource("dense",ScheduleAccessMode.Write);
            var split=ScheduleGraph.Compile(registry,tasks.Select(t=>t with{Access=[resource]}),[new("dense")]);
            Check(split.PhaseAnchorCount==1 && split.EdgeCount==8129 && split.EdgeCount<=ScheduleGraph.MaxEdges);
            Check(ScheduleGraph.Compile(registry,[]).PhaseAnchorCount==0);
        });
        yield return ("E6-C read-read independent; component read-write and write-write ordered", () => {
            var registry = global::Ncma.Runtime.CompositionRegistry.Build(); var read = ScheduleAccess.Read<TransformData>(registry); var write = ScheduleAccess.Write<TransformData>(registry);
            Check(ScheduleGraph.Compile(registry, [Task("b", read), Task("a", read)]).EdgeCount == 0);
            foreach (var access in new[] { read, write }) {
                var graph = ScheduleGraph.Compile(registry, [Task("b", access), Task("a", write)]);
                Check(graph.CopySerialOrder().SequenceEqual(new[] { "a", "b" }) && graph.CopyEdges().Single().Reason.StartsWith("component:"));
            }
        });
        yield return ("E6-C explicit reverse dependency wins over ordinal conflict order", () => {
            var registry = global::Ncma.Runtime.CompositionRegistry.Build(); var write = ScheduleAccess.Write<TransformData>(registry);
            var graph = ScheduleGraph.Compile(registry, [Task("a", write) with { After = ["z"] }, Task("z", write)]);
            Check(graph.CopySerialOrder().SequenceEqual(new[] { "z", "a" }) && graph.EdgeCount == 1);
        });
        yield return ("E6-C phases enforce order and reject contradictory explicit order or cycles", () => {
            var registry = global::Ncma.Runtime.CompositionRegistry.Build();
            Check(ScheduleGraph.Compile(registry, [Task("a") with { Phase = 1 }, Task("z")]).CopySerialOrder().SequenceEqual(new[] { "z", "a" }));
            Reject(() => ScheduleGraph.Compile(registry, [Task("a") with { Phase = 1, Before = ["z"] }, Task("z")]));
            Reject(() => ScheduleGraph.Compile(registry, [Task("a") with { After = ["b"] }, Task("b") with { After = ["a"] }]));
        });
        yield return ("E6-C external resources conflict and require explicit worker-safe ownership", () => {
            var registry = global::Ncma.Runtime.CompositionRegistry.Build(); var read = ScheduleAccess.Resource("physics.solver", ScheduleAccessMode.Read);
            var write = ScheduleAccess.Resource("physics.solver", ScheduleAccessMode.Write);
            var graph = ScheduleGraph.Compile(registry, [Task("read", read), Task("write", write)], [new("physics.solver")]);
            Check(graph.CopyEdges().Single().Reason == "resource:physics.solver");
            Reject(() => ScheduleGraph.Compile(registry, [Task("worker", read) with { Affinity = ScheduleAffinity.Worker }], [new("physics.solver")]));
            Check(ScheduleGraph.Compile(registry, [Task("worker", read) with { Affinity = ScheduleAffinity.Worker }], [new("physics.solver", true)]).TaskCount == 1);
            Reject(() => ScheduleGraph.Compile(registry, [Task("read", read)]));
        });
        yield return ("E6-C structure conservatively orders all tasks and stays owner-thread", () => {
            var registry = global::Ncma.Runtime.CompositionRegistry.Build();
            var graph = ScheduleGraph.Compile(registry, [Task("a"), Task("structure", ScheduleAccess.StructuralWrite()), Task("z")]);
            Check(graph.EdgeCount == 2 && graph.CopyEdges().All(e => e.Reason == "structure"));
            Reject(() => ScheduleGraph.Compile(registry, [Task("bad", ScheduleAccess.StructuralWrite()) with { Affinity = ScheduleAffinity.Worker }]));
        });
        yield return ("E6-C copies and fingerprint do not depend on registration or caller mutation", () => {
            var registry = global::Ncma.Runtime.CompositionRegistry.Build(); var read = ScheduleAccess.Read<TransformData>(registry);
            var a = Task("a", read); var b = Task("b", read) with { After = ["a"] };
            var graph = ScheduleGraph.Compile(registry, [a, b]);
            Check(graph.ContentHash == ScheduleGraph.Compile(registry, [b, a]).ContentHash);
            a.Access[0] = ScheduleAccess.StructuralWrite(); b.After[0] = "missing";
            var copies = graph.CopyTasks(); copies[0].Access[0] = ScheduleAccess.StructuralWrite(); copies[1].After[0] = "missing";
            var edges = graph.CopyEdges(); edges[0] = new("bad", "bad", "bad"); var order = graph.CopySerialOrder(); order[0] = "bad";
            Check(graph.CopyTasks()[0].Access[0] == read && graph.CopyTasks()[1].After.Single() == "a" && graph.CopySerialOrder()[0] == "a" && graph.CopyEdges()[0].From == "a");
        });
        yield return ("E6-C duplicate unknown and schema-mismatched declarations rejected", () => {
            var registry = global::Ncma.Runtime.CompositionRegistry.Build(); var read = ScheduleAccess.Read<TransformData>(registry);
            Reject(() => ScheduleGraph.Compile(registry, [Task("a"), Task("a")]));
            Reject(() => ScheduleGraph.Compile(registry, [Task("a", read, read)]));
            Reject(() => ScheduleGraph.Compile(registry, [Task("a", read with { Version = read.Version + 1 })]));
            Reject(() => ScheduleGraph.Compile(registry, [Task("a", read with { Id = "missing" })]));
            Reject(() => ScheduleGraph.Compile(registry, [Task("a") with { Before = ["missing"] }]));
            Reject(() => ScheduleGraph.Compile(registry, [Task("a") with { Before = ["a"] }]));
            Reject(() => ScheduleGraph.Compile(registry, [Task("a")], [new("r"), new("r")]));
        });
        yield return ("E6-C task access phase link and identifier budgets are bounded", () => {
            var registry = global::Ncma.Runtime.CompositionRegistry.Build();
            Reject(() => ScheduleGraph.Compile(registry, Enumerable.Range(0, ScheduleGraph.MaxTasks + 1).Select(i => Task("t" + i))));
            Reject(() => ScheduleGraph.Compile(registry, [Task("a", Enumerable.Repeat(ScheduleAccess.StructuralWrite(), ScheduleGraph.MaxAccessPerTask + 1).ToArray())]));
            Reject(() => ScheduleGraph.Compile(registry, [Task("a") with { Phase = ScheduleGraph.MaxPhases }]));
            Reject(() => ScheduleGraph.Compile(registry, [Task("a") with { Before = Enumerable.Repeat("b", ScheduleGraph.MaxLinksPerTask + 1).ToArray() }]));
            Reject(() => ScheduleGraph.Compile(registry, [Task(new string('a',129))]));
            Check(ScheduleGraph.Compile(registry, []).CopySerialOrder().Length == 0);
        });
    }
}
