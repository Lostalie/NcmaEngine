import copy
import unittest
import test_ecs_e5d_loop_evidence as previous
from ncma_tools.ecs_e5d_pacing_evidence import verify


def fixture():
    data=previous.fixture()
    data.update(Version=2, FrameRateLimit=100)
    for row in data["Samples"]:
        row["PacingAdmissionTimestamp"]=row["StartedTimestamp"]+1
        row["Phases"].update(Control=0, Pacing=1)
    return data


class PacingEvidenceTests(unittest.TestCase):
    def test_cap_and_uncapped(self):
        self.assertEqual(verify(fixture())["retainedAdmissions"], 3)
        data=fixture()
        data["FrameRateLimit"]=None
        for row in data["Samples"]:
            row["PacingAdmissionTimestamp"]=None
            row["Phases"].update(Control=1, Pacing=0)
        self.assertIsNone(verify(data)["minimumAdmissionTicks"])
        def camel(value):
            if isinstance(value, dict):
                return {k[0].lower()+k[1:]: camel(v) for k,v in value.items()}
            return [camel(v) for v in value] if isinstance(value, list) else value
        self.assertEqual(verify(camel(fixture()), camel_case=True), verify(fixture()))

    def test_cancelled_wait(self):
        data=fixture()
        data["Samples"][1]["PacingAdmissionTimestamp"]=None
        self.assertEqual(verify(data)["retainedAdmissions"], 2)

    def test_bad_admission_and_metadata(self):
        changes=[
            lambda d:d.update(Version=1),
            lambda d:d.update(FrameRateLimit=True),
            lambda d:d.update(FrameRateLimit=241),
            lambda d:d.update(FrameRateLimit=0),
            lambda d:d.update(FrameRateLimit=30),
            lambda d:d.update(FrameRateLimit=None),
            lambda d:d["Samples"][0].pop("PacingAdmissionTimestamp"),
            lambda d:d["Samples"][0].update(PacingAdmissionTimestamp=None),
            lambda d:d["Samples"][0].update(PacingAdmissionTimestamp=True),
            lambda d:d["Samples"][0].update(PacingAdmissionTimestamp=0),
            lambda d:d["Samples"][0].update(PacingAdmissionTimestamp=21),
            lambda d:d["Samples"][0]["Phases"].update(Pacing=-1),
            lambda d:d["Samples"][0]["Phases"].update(Pacing=True),
            lambda d:d["Samples"][0]["Phases"].pop("Pacing"),
            lambda d:d["Samples"][0]["Phases"].update(Control=True),
            lambda d:d["Samples"][0]["Phases"].update(Pacing=2),
        ]
        for change in changes:
            data=copy.deepcopy(fixture())
            change(data)
            with self.subTest(change=change), self.assertRaises(ValueError):
                verify(data)
