using Ncma.Rendering;

// Test-only reconstruction from copied reports; no GPU reads and no timed-frame work.
internal sealed class EcsGpuSnapshotLedger
{
    private readonly ulong _generation;
    private ulong _frame;
    private readonly HashSet<ulong> _frames;
    private readonly Dictionary<(GpuTimingDomain,ulong),Entry> _seen=[];
    private readonly List<Entry> _resolved=[];
    private readonly Dictionary<GpuTimingDomain,Track> _tracks=[];
    private int _duplicates;
    private sealed record Entry(ulong Generation,GpuTimingDomain Domain,ulong Sequence,ulong OriginFrame,
        GpuSampleState State,ulong Resource,ulong ContentRevision,double Milliseconds,bool InMeasuredWindow);
    private sealed class Track(GpuWorkTimingSample current,GpuWorkTimingSample resolved)
    {
        internal GpuWorkTimingSample Current=current,Resolved=resolved;
        internal ulong AdmissionGaps,ResolutionGaps;
        internal readonly int[] CurrentStates=new int[8],ResolvedStates=new int[8];
    }
    internal EcsGpuSnapshotLedger(PresentationCostReport baseline,HashSet<ulong> frames)
    {
        _generation=baseline.Generation;_frame=baseline.HostFrame;_frames=frames;
        ValidateReport(baseline);
        if(frames.Count==0||frames.Any(f=>f<=_frame))throw new InvalidOperationException("GPU baseline must precede measured frames");
        foreach(var domain in Enum.GetValues<GpuTimingDomain>()){
            var(c,r)=Pair(baseline,domain);_tracks.Add(domain,new(c,r));
            if(r.Timing.Sequence!=0)_seen.Add((domain,r.Timing.Sequence),EntryOf(r));
        }
    }
    private void ValidateReport(PresentationCostReport report)
    {
        if(report.Version!=2||!report.Completed||report.Generation==0||report.Generation!=_generation||report.HostFrame==0)
            throw new InvalidOperationException("GPU snapshot report identity");
    }
    private static GpuWorkTimingSample Source(PresentationCostReport report,AttributedGpuCost? observation,GpuTimingDomain domain)
    {
        if(observation is not{} a)throw new InvalidOperationException("Missing GPU snapshot slot");
        var s=a.Sample;var t=s.Timing;
        var attribution=!t.IsValid?CostSampleAttribution.NoValidSample:t.OriginFrame==report.HostFrame?
            CostSampleAttribution.CurrentSourceFrame:CostSampleAttribution.EarlierSourceFrame;
        if(!Enum.IsDefined(t.State)||t.Generation!=report.Generation||t.Domain!=domain||t.OriginFrame>report.HostFrame||t.ObservedFrame>report.HostFrame||
            !double.IsFinite(t.Milliseconds)||t.Milliseconds<0||(!t.IsValid&&t.Milliseconds!=0)||a.Attribution!=attribution||
            (t.Sequence==0)!=(t.OriginFrame==0)||(t.State is >=GpuSampleState.Recording and <=GpuSampleState.Invalid&&t.Sequence==0)||
            (t.State==GpuSampleState.Unavailable&&t.Sequence!=0)||(t.State is >=GpuSampleState.Pending and <=GpuSampleState.Invalid&&t.ObservedFrame<t.OriginFrame)||
            (domain!=GpuTimingDomain.UiProduction?(s.Resource!=0||s.ContentRevision!=0):
                t.Sequence==0?(s.Resource!=0||s.ContentRevision!=0):(s.Resource==0||s.ContentRevision==0)))
            throw new InvalidOperationException("Invalid GPU snapshot provenance");
        return s;
    }
    private static (GpuWorkTimingSample Current,GpuWorkTimingSample Resolved) Pair(PresentationCostReport p,GpuTimingDomain domain)
    {
        var(c,r)=domain switch{
            GpuTimingDomain.MainSubmissionBeforePresent=>(p.Main,p.MainResolved),
            GpuTimingDomain.SkinDispatch=>(p.Skin,p.SkinResolved),
            _=>(p.Ui,p.UiResolved)};
        var current=Source(p,c,domain);var resolved=Source(p,r,domain);
        if(resolved.Timing.State is not(GpuSampleState.Unavailable or GpuSampleState.Valid or GpuSampleState.Disjoint or GpuSampleState.Invalid)||
            resolved.Timing.Sequence>current.Timing.Sequence||resolved.Timing.ObservedFrame!=current.Timing.ObservedFrame||
            resolved.Timing.SkippedSamples>current.Timing.SkippedSamples||
            (current.Timing.State is GpuSampleState.Recording or GpuSampleState.Pending&&resolved.Timing.Sequence==current.Timing.Sequence)||
            (current.Timing.State is GpuSampleState.Valid or GpuSampleState.Disjoint or GpuSampleState.Invalid&&current!=resolved))
            throw new InvalidOperationException("Inconsistent GPU snapshot pair");
        return(current,resolved);
    }
    private Entry EntryOf(GpuWorkTimingSample s)=>new(s.Timing.Generation,s.Timing.Domain,s.Timing.Sequence,s.Timing.OriginFrame,
        s.Timing.State,s.Resource,s.ContentRevision,s.Timing.Milliseconds,_frames.Contains(s.Timing.OriginFrame));
    internal void Add(PresentationCostReport report)
    {
        ValidateReport(report);
        if(report.HostFrame<=_frame||!_frames.Contains(report.HostFrame))throw new InvalidOperationException("GPU report order");
        // Validate all domains before mutating the ledger.
        var pairs=Enum.GetValues<GpuTimingDomain>().Select(d=>(Domain:d,Pair:Pair(report,d))).ToArray();
        foreach(var(d,(c,r)) in pairs){
            var old=_tracks[d];var ct=c.Timing;var rt=r.Timing;
            if(ct.Sequence<old.Current.Timing.Sequence||rt.Sequence<old.Resolved.Timing.Sequence||
                ct.SkippedSamples<old.Current.Timing.SkippedSamples||ct.ObservedFrame<old.Current.Timing.ObservedFrame||
                (ct.Sequence==old.Current.Timing.Sequence&&(ct.OriginFrame!=old.Current.Timing.OriginFrame||c.Resource!=old.Current.Resource||c.ContentRevision!=old.Current.ContentRevision))||
                (rt.Sequence!=0&&_seen.TryGetValue((d,rt.Sequence),out var existing)&&existing!=EntryOf(r)))
                throw new InvalidOperationException("Regressed or conflicting GPU snapshot");
        }
        foreach(var(d,(c,r)) in pairs){
            var old=_tracks[d];var ct=c.Timing;var rt=r.Timing;
            ulong admission=ct.Sequence-old.Current.Timing.Sequence,resolution=rt.Sequence-old.Resolved.Timing.Sequence;
            old.AdmissionGaps+=admission>0?admission-1:0;old.ResolutionGaps+=resolution>0?resolution-1:0;
            old.CurrentStates[(int)ct.State]++;old.ResolvedStates[(int)rt.State]++;
            if(rt.Sequence!=0){
                var key=(d,rt.Sequence);var entry=EntryOf(r);
                if(_resolved.Any(e=>e.Domain==d&&e.Sequence==rt.Sequence))_duplicates++;
                else _resolved.Add(entry);
                _seen[key]=entry;
            }
            old.Current=c;old.Resolved=r;
        }
        _frame=report.HostFrame;
    }
    internal object Summary()=>new{
        resolved=_resolved.ToArray(),duplicateResolvedObservations=_duplicates,
        domains=Enum.GetValues<GpuTimingDomain>().Select(d=>new{
            domain=d,uniqueMeasuredValidSources=_resolved.Count(e=>e.Domain==d&&e.State==GpuSampleState.Valid&&e.InMeasuredWindow),
            uniqueOutsideWindowValidSources=_resolved.Count(e=>e.Domain==d&&e.State==GpuSampleState.Valid&&!e.InMeasuredWindow),
            uniqueInvalidResolvedSources=_resolved.Count(e=>e.Domain==d&&e.State!=GpuSampleState.Valid),
            currentStates=_tracks[d].CurrentStates,resolvedStates=_tracks[d].ResolvedStates,
            admissionSequenceGaps=_tracks[d].AdmissionGaps,unobservedResolutionSequenceSlots=_tracks[d].ResolutionGaps,
            unobservedTailSequenceSlots=_tracks[d].Current.Timing.Sequence-_tracks[d].Resolved.Timing.Sequence}).ToArray(),
        completeGpuCoverage=false,scope="last-resolved sources only; current status retained; sequence gaps may include abandoned work; no final drain or cross-domain sum"};
}
