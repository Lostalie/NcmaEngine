import copy
import unittest
from ncma_tools.ecs_e5d_stage_evidence import STAGES,SCOPE,IDENTITY,verify

def fixture():
    phases=[dict(Stage=i,Visits=1 if i==0 else 0,WallTicks=20 if i==0 else 0,
        OwnerAllocatedBytes=64 if i==0 else 0) for i in range(len(STAGES))]
    return dict(Version=1,TimestampFrequency=1000,Capacity=64,TotalCalls=1,OmittedCalls=0,
        Calls=[dict(Sequence=1,FrameHint=1,Call=1,Completed=True,DiagnosticCaptureRequested=False,
            StartedTimestamp=10,EndedTimestamp=30,OwnerAllocatedBytes=64,Stages=phases)],
        CompleteRawCoverage=True,PerformanceAccepted=False,Scope=SCOPE,Identity=IDENTITY)

class StageEvidenceTests(unittest.TestCase):
    def test_valid_and_empty(self):
        data=fixture();self.assertEqual(verify(data)["maximumRecordedCallMs"],20)
        data.update(TotalCalls=0,Calls=[]);self.assertEqual(verify(data)["retainedCalls"],0)

    def test_camel_strings(self):
        def camel(value):
            if isinstance(value,dict):return {k[0].lower()+k[1:]:camel(v) for k,v in value.items()}
            if isinstance(value,list):return list(map(camel,value))
            return value
        data=camel(fixture());data["calls"][0]["call"]="Present"
        for i,phase in enumerate(data["calls"][0]["stages"]):phase["stage"]=STAGES[i]
        self.assertEqual(verify(data,camel_case=True)["totalCalls"],1)

    def test_repeated_hint_fault_and_omissions(self):
        data=fixture();second=copy.deepcopy(data["Calls"][0])
        second.update(Sequence=2,StartedTimestamp=40,EndedTimestamp=60,Completed=False)
        data.update(Calls=[data["Calls"][0],second],Capacity=2,TotalCalls=5,OmittedCalls=3,CompleteRawCoverage=False)
        self.assertEqual(verify(data)["failedCalls"],1)

    def test_reject_mutations(self):
        mutations=[
            lambda d:d.update(Version=True),lambda d:d.update(Version=2),
            lambda d:d.update(TimestampFrequency=0),lambda d:d.update(Capacity=65),
            lambda d:d.update(TotalCalls=2),lambda d:d.update(OmittedCalls=1),
            lambda d:d.update(CompleteRawCoverage=False),lambda d:d.update(PerformanceAccepted=True),
            lambda d:d.update(Scope="GPU time"),lambda d:d["Calls"][0].update(Sequence=2),
            lambda d:d["Calls"][0].update(FrameHint=0),lambda d:d["Calls"][0].update(Completed=1),
            lambda d:d["Calls"][0].update(EndedTimestamp=9),lambda d:d["Calls"][0].update(OwnerAllocatedBytes=-1),
            lambda d:d["Calls"][0].update(Call=True),lambda d:d["Calls"][0]["Stages"].pop(),
            lambda d:d["Calls"][0]["Stages"][0].update(Visits=0),
            lambda d:d["Calls"][0]["Stages"][1].update(Stage=0),
            lambda d:d["Calls"][0]["Stages"][1].update(WallTicks=1),
            lambda d:d["Calls"][0]["Stages"][0].update(OwnerAllocatedBytes=63),
            lambda d:d["Calls"][0]["Stages"][3].update(Visits=1),
            lambda d:d["Calls"][0].update(StartedTimestamp=11),
        ]
        for index,mutate in enumerate(mutations):
            with self.subTest(index=index):
                data=fixture();mutate(data)
                with self.assertRaises(ValueError):verify(data)
