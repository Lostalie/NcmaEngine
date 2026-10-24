import copy
import unittest
from ncma_tools.ecs_e5d_loop_evidence import verify


def fixture():
    phases = dict(Control=1, Platform=1, Intents=1, Agent=1, BeginPresentation=1,
                  Input=1, Simulation=1, Presentation=2, Recovery=1)
    return dict(Version=1, TimestampFrequency=1000, Capacity=512, TotalIterations=3,
                OmittedIterations=0, PresentReturns=2, PresentIntervals=1,
                MaximumPresentIntervalTicks=40, CompleteRawCoverage=True, TargetPerformanceAccepted=False,
                Scope="owner-loop-wall-including-observation; present-return-not-display; startup/shutdown excluded",
                Samples=[dict(Sequence=i+1, FrameId=i+1, Outcome=["Completed", "Faulted", "Recovered"][i],
                              StartedTimestamp=10+i*20, EndedTimestamp=20+i*20, OwnerAllocatedBytes=0,
                              Phases=phases.copy(), PresentReturnTimestamp=None if i == 1 else 19+i*20,
                              PresentReturnIntervalTicks=40 if i == 2 else None) for i in range(3)])


class LoopEvidenceTests(unittest.TestCase):
    def test_complete_includes_fault_and_gap(self):
        self.assertEqual(verify(fixture())["presentReturns"], 2)
        def camel(value):
            if isinstance(value, dict):
                return {k[0].lower()+k[1:]: camel(v) for k, v in value.items()}
            return [camel(v) for v in value] if isinstance(value, list) else value
        self.assertEqual(verify(camel(fixture()), camel_case=True), verify(fixture()))

    def test_omitted_suffix_is_not_complete(self):
        data = fixture()
        data.update(Capacity=2, OmittedIterations=1, CompleteRawCoverage=False)
        data["Samples"] = data["Samples"][:2]
        self.assertEqual(verify(data)["omitted"], 1)
        data["CompleteRawCoverage"] = True
        with self.assertRaises(ValueError):
            verify(data)

    def test_corruption_rejected(self):
        mutations = [
            lambda d: d.update(TargetPerformanceAccepted=True),
            lambda d: d.update(Version=True),
            lambda d: d.update(TimestampFrequency=0),
            lambda d: d.update(Capacity=513),
            lambda d: d.update(TotalIterations=4),
            lambda d: d.update(OmittedIterations=1),
            lambda d: d.update(PresentReturns=3),
            lambda d: d.update(PresentIntervals=0),
            lambda d: d.update(MaximumPresentIntervalTicks=39),
            lambda d: d["Samples"][0].update(Sequence=2),
            lambda d: d["Samples"][0].update(Outcome=True),
            lambda d: d["Samples"][0].update(OwnerAllocatedBytes=-1),
            lambda d: d["Samples"][0].update(PresentReturnTimestamp=21),
            lambda d: d["Samples"][1].update(PresentReturnIntervalTicks=0),
            lambda d: d["Samples"][2].update(PresentReturnIntervalTicks=39),
            lambda d: d["Samples"][1]["Phases"].update(Recovery=0),
            lambda d: d["Samples"][1].pop("Phases"),
            lambda d: d["Samples"][0].update(FrameId=False),
        ]
        for mutation in mutations:
            data = copy.deepcopy(fixture())
            mutation(data)
            with self.subTest(mutation=mutation), self.assertRaises(ValueError):
                verify(data)
