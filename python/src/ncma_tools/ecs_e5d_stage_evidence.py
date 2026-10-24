"""Validate bounded presentation-call partitions. Never accepts target performance."""
import json
from pathlib import Path

STAGES=("Control","Observer","Surface","GuiBegin","GuiBuild","ViewPreparation","GuiDraw",
        "SceneSubmission","GuiGpu","DiagnosticCapture","NativePresent","Cleanup","Diagnostics")
CALLS=("BeginPresentation","Present")
SCOPE="host presentation call wall/owner allocation; sequential stages including nested work/observation; not GPU time or upload bytes; excludes call-entry JIT/readiness guards/startup/shutdown"
IDENTITY="sequence is unique; frame hint may repeat after suspend/fault/recovery, and is not a GPU source token"

def verify(report, *, camel_case=False):
    def key(name):return name[0].lower()+name[1:] if camel_case else name
    def get(obj,name):
        if not isinstance(obj,dict) or key(name) not in obj:raise ValueError("Missing "+name)
        return obj[key(name)]
    def number(value,maximum=(1<<63)-1):
        if type(value) is not int or not 0<=value<=maximum:raise ValueError("Invalid integer metric")
        return value
    def boolean(value):
        if type(value) is not bool:raise ValueError("Invalid boolean")
        return value
    def enum(value,names):
        if type(value) is int and 0<=value<len(names):return names[value]
        if type(value) is str and value in names:return value
        raise ValueError("Invalid enum")
    if type(get(report,"Version")) is not int or get(report,"Version")!=1:raise ValueError("Version")
    if get(report,"Scope")!=SCOPE or get(report,"Identity")!=IDENTITY:raise ValueError("Evidence scope")
    frequency=number(get(report,"TimestampFrequency"))
    capacity=number(get(report,"Capacity"))
    total=number(get(report,"TotalCalls"),(1<<64)-1)
    omitted=number(get(report,"OmittedCalls"),(1<<64)-1)
    if frequency==0 or not 1<=capacity<=64:raise ValueError("Frequency/capacity")
    rows=get(report,"Calls")
    if not isinstance(rows,list) or len(rows)!=min(capacity,total) or omitted!=total-len(rows):raise ValueError("Prefix/omissions")
    if boolean(get(report,"PerformanceAccepted")) or boolean(get(report,"CompleteRawCoverage"))!=(omitted==0):raise ValueError("Acceptance/coverage")
    last_end=0;failed=0;times=[]
    phase_totals={stage:0 for stage in STAGES}
    for index,row in enumerate(rows):
        if number(get(row,"Sequence"),(1<<64)-1)!=index+1 or number(get(row,"FrameHint"),(1<<64)-1)==0:raise ValueError("Identity")
        call=enum(get(row,"Call"),CALLS)
        completed=boolean(get(row,"Completed"));boolean(get(row,"DiagnosticCaptureRequested"))
        failed+=not completed
        started=number(get(row,"StartedTimestamp"));ended=number(get(row,"EndedTimestamp"))
        allocated=number(get(row,"OwnerAllocatedBytes"))
        if started<last_end or ended<started:raise ValueError("Nonmonotonic/overlapping calls")
        last_end=ended;times.append(1000*(ended-started)/frequency)
        phases=get(row,"Stages")
        if not isinstance(phases,list) or len(phases)!=len(STAGES):raise ValueError("Incomplete stages")
        ticks=allocations=0
        for phase_index,phase in enumerate(phases):
            name=enum(get(phase,"Stage"),STAGES)
            if name!=STAGES[phase_index]:raise ValueError("Duplicate/reordered stage")
            visits=number(get(phase,"Visits"),(1<<31)-1)
            wall=number(get(phase,"WallTicks"));allocation=number(get(phase,"OwnerAllocatedBytes"))
            if visits==0 and (wall!=0 or allocation!=0):raise ValueError("Cost without phase entry")
            if name=="Control" and visits==0:raise ValueError("Missing initial control")
            if call=="BeginPresentation" and name not in ("Control","GuiBegin") and visits:raise ValueError("Invalid begin phase")
            if call=="Present" and name=="GuiBegin" and visits:raise ValueError("Invalid present phase")
            ticks+=wall;allocations+=allocation;phase_totals[name]+=wall
        if ticks!=ended-started or allocations!=allocated:raise ValueError("Partition mismatch")
    return dict(retainedCalls=len(rows),totalCalls=total,omittedCalls=omitted,failedCalls=failed,
        maximumRecordedCallMs=max(times,default=None),recordedStageTicks=phase_totals,
        performanceAccepted=False)

def read(path, *, camel_case=False):
    path=Path(path)
    if path.stat().st_size>1024*1024:raise ValueError("Evidence budget")
    return verify(json.loads(path.read_text(encoding="utf-8-sig")),camel_case=camel_case)
