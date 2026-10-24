import copy
import unittest

from ncma_tools.ecs_e5d_domain_evidence import validate, CONTRACT, NATIVE


class EcsDomainEvidenceTests(unittest.TestCase):
    def fixture(self):
        windows = []
        for mode in range(3):
            rows, unique = [], []
            for frame in range(1, 101):
                native = {k: 0 for k in NATIVE}
                native.update(Sequence=frame, LiveBodies=2, LastStepMilliseconds=.1)
                a = dict(SessionId="00000000-0000-0000-0000-000000000001", WorldId="00000000-0000-0000-0000-000000000002", State=2, Kind=0,
                         Completed=True, IncludesUpdate=True, TickBefore=frame-1, TickAfter=frame, AdvanceSequence=frame, CommittedSteps=1,
                         RequestedDeltaSeconds=1/60, FullPerformanceAccepted=False, CpuWallMilliseconds=.4, OwnerThreadAllocatedBytes=80)
                s = dict(Advance=a, LastCharacterStep=dict(PlaySessionId=a["SessionId"], WorldId=a["WorldId"], Tick=frame, Native=native), FullPerformanceAccepted=False)
                p = dict(Generation=mode+1, HostFrame=frame, Completed=True, IncludesDiagnosticCapture=False, FullPerformanceAccepted=False, CpuWallMilliseconds=.5, OwnerThreadAllocatedBytes=90,
                         Counters=dict(MainSubmissions=1, Presents=1, MeshUploadedBytes=0, ResourceUploadedBytes=0, EnvironmentUploadedBytes=0, UiUploadedBytes=0))
                for name, domain in (("Main", 1), ("Skin", 2), ("Ui", 3)):
                    valid = domain == 1
                    t = dict(Generation=mode+1, Domain=domain, Sequence=frame if valid else 0, OriginFrame=frame if valid else 0, ObservedFrame=frame,
                             SkippedSamples=0, State=3 if valid else 0, Milliseconds=.3 if valid else 0, IsValid=valid)
                    p[name] = dict(Sample=dict(Timing=t, Resource=0, ContentRevision=0), Attribution=1 if valid else 0)
                unique.append(dict(Generation=mode+1, Domain=1, Sequence=frame, OriginFrame=frame, Resource=0, ContentRevision=0, Milliseconds=.3, InMeasuredWindow=True))
                rows.append(dict(HostMilliseconds=1, HostOwnerAllocatedBytes=200, Simulation=s, Presentation=p))
            memory = dict(WorkingSetBytes=1000, PrivateBytes=2000, RawManagedHeapBytes=300, RawManagedHeapValid=True, LastGcIndex=1, LastGcHeapSizeBytes=250, LastGcFragmentedBytes=20, Gen0=1, Gen1=1, Gen2=1)
            resources = {k: 0 for k in ("Groups", "Meshes", "SkinMeshes", "UiTargets", "Environments", "Textures", "Materials", "Targets", "PostPrograms", "PhysicsWorlds")}
            windows.append(dict(label=f"editor-running-mode-{mode}", warmup=12, samples=100, fixedInputDeltaSeconds=1/60, rows=rows,
                memoryBefore=memory, memoryAfter=copy.deepcopy(memory), resourcesBefore=resources, resourcesAfter=copy.deepcopy(resources), cpuScopesAreNested=True, gpuDomainsAreNotAdditive=True,
                syntheticInputClock=True, framePacingMeasured=False, targetPerformanceAccepted=False, memoryAcceptance="pending",
                gpu=dict(unique=unique, duplicateObservations=0, invalidOrPendingObservations=200, completeGpuCoverage=False,
                         domains=[dict(domain=d, uniqueMeasuredSources=100 if d == 1 else 0, uniqueOutsideWindowSources=0) for d in (1, 2, 3)])))
        return dict(schema=2, contractId=CONTRACT, host="Editor", windows=windows, developmentOnly=True, formalCohort=False, targetPerformanceAccepted=False, memoryAcceptance="pending")

    def test_complete_development_scope_and_signed_heap(self):
        data = self.fixture()
        self.assertEqual(validate(data)["samples"], 300)
        data["windows"][0]["memoryBefore"].update(RawManagedHeapBytes=-9, RawManagedHeapValid=False)
        self.assertEqual(validate(data)["memoryAcceptance"], "pending")

    def test_scope_native_and_gpu_mutations_reject(self):
        base = self.fixture()
        mutations = [
            lambda d: d.update(targetPerformanceAccepted=True),
            lambda d: d.update(formalCohort=True),
            lambda d: d.update(schema=1),
            lambda d: d["windows"].pop(),
            lambda d: d["windows"][0].update(framePacingMeasured=True),
            lambda d: d["windows"][0]["rows"].pop(),
            lambda d: d["windows"][0]["resourcesAfter"].update(Meshes=1),
            lambda d: d["windows"][0]["memoryBefore"].update(RawManagedHeapBytes=-9),
            lambda d: d["windows"][0]["rows"][0]["Simulation"]["LastCharacterStep"].update(Native={}),
            lambda d: d["windows"][0]["rows"][0]["Simulation"]["LastCharacterStep"]["Native"].update(Sequence=9),
            lambda d: d["windows"][0]["rows"][0]["Simulation"]["LastCharacterStep"]["Native"].update(LiveJobs=1),
            lambda d: d["windows"][0]["rows"][0]["Simulation"]["Advance"].update(TickAfter=2),
            lambda d: d["windows"][0]["rows"][1]["Simulation"]["Advance"].update(AdvanceSequence=1),
            lambda d: d["windows"][0]["rows"][0].update(HostMilliseconds=True),
            lambda d: d["windows"][0]["rows"][0].update(HostMilliseconds=float("nan")),
            lambda d: d["windows"][0]["rows"][0]["Presentation"].update(IncludesDiagnosticCapture=True),
            lambda d: d["windows"][0]["rows"][0]["Presentation"]["Main"].update(Attribution=2),
            lambda d: d["windows"][0]["rows"][0]["Presentation"]["Main"]["Sample"]["Timing"].update(Generation=99),
            lambda d: d["windows"][0]["rows"][0]["Presentation"]["Main"]["Sample"]["Timing"].update(OriginFrame=2),
            lambda d: d["windows"][0]["rows"][0]["Presentation"]["Main"]["Sample"]["Timing"].update(State=2),
            lambda d: d["windows"][0]["gpu"].update(duplicateObservations=1),
            lambda d: d["windows"][0]["gpu"]["unique"].pop(),
            lambda d: d["windows"][0]["gpu"]["domains"][0].update(uniqueMeasuredSources=99),
        ]
        for index, mutate in enumerate(mutations):
            with self.subTest(index=index):
                data = copy.deepcopy(base); mutate(data)
                with self.assertRaises((ValueError, KeyError)):
                    validate(data)
