@{
    SchemaVersion = 1
    HistoricalPolicy = 'M8_5-TestGroups.psd1'
    HistoricalCount = 159
    ExpectedCount = 180
    # Contract entries are NOT real SDK/native/job integration acceptance.
    Workflow = @(
        @{Name='NcmaWorkflowTests.Contracts';Switch='--workflow-contract-tests';Timeout=30;Role='contracts'}
        @{Name='NcmaWorkflowTests.Lifecycle';Switch='--workflow-job-lifecycle-tests';Timeout=30;Role='contracts'}
        @{Name='NcmaWorkflowTests.Output';Switch='--workflow-build-output-tests';Timeout=30;Role='contracts'}
        @{Name='NcmaWorkflowTests.Restore';Switch='--workflow-restore-inspection-tests';Timeout=30;Role='contracts'}
        @{Name='NcmaWorkflowTests.Toolchain';Switch='--workflow-toolchain-tests';Timeout=30;Role='contracts'}
        @{Name='NcmaWorkflowTests.Offline';Switch='--workflow-offline-preparation-tests';Timeout=30;Role='contracts'}
        @{Name='NcmaWorkflowTests.Observation';Switch='--workflow-observation-tests';Timeout=30;Role='contracts'}
        @{Name='NcmaWorkflowTests.ManagedClosure';Switch='--workflow-runtime-closure-tests';Timeout=30;Role='contracts'}
        @{Name='NcmaWorkflowTests.Staging';Switch='--workflow-host-staging-tests';Timeout=30;Role='contracts'}
        @{Name='NcmaWorkflowTests.Native';Switch='--workflow-native-preparation-tests';Timeout=30;Role='contracts'}
        @{Name='NcmaWorkflowTests.CaptureContracts';Switch='--workflow-capture-contract-tests';Timeout=30;Role='contracts'}
        @{Name='NcmaWorkflowTests.CaptureHost';Switch='--workflow-capture-bridge-tests';Timeout=180;Role='real-preparer'}
        @{Name='NcmaWorkflowTests.OriginalHost';Switch='--workflow-host-tests';Timeout=180;Role='original-host'}
        @{Name='NcmaWorkflowTests.ContinuationHost';Switch='--workflow-continuation-tests';Timeout=180;Role='continuation-host'}
        @{Name='NcmaWorkflowTests.SourceCreationContracts';Switch='--workflow-source-capture-contract-tests';Timeout=30;Role='contracts'}
        @{Name='NcmaWorkflowTests.SourceCreationHost';Switch='--workflow-source-capture-host-tests';Timeout=180;Role='source-creation-host'}
        @{Name='NcmaWorkflowTests.SdkInputContracts';Switch='--workflow-sdk-input-contract-tests';Timeout=30;Role='contracts'}
        @{Name='NcmaWorkflowTests.SdkChain';Switch='--workflow-sdk-chain-tests';Timeout=3900;Role='real-sdk-chain'}
        @{Name='NcmaWorkflowTests.DomainSourceHost';Switch='--workflow-domain-source-tests';Timeout=180;Role='domain-source-host'}
        @{Name='NcmaWorkflowTests.CaptureInputs';Script='Test-WorkflowBuildInputs.ps1';Timeout=60;Role='capture-inputs'}
        @{Name='NcmaWorkflowTests.InventoryContracts';Script='Test-M8_6Inventory.ps1';Timeout=30;Role='inventory-contracts'}
    )
}
