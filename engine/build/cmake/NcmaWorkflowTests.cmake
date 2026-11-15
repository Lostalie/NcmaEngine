# M8.6 entries are additive. Never change old entry names, timeouts or filters.
# An empty capture ID deliberately fails the registered host entry at runtime.
set(NCMA_WORKFLOW_WORKSPACE_ROOT "${CMAKE_SOURCE_DIR}" CACHE PATH "Explicit canonical workspace for M8.6 test IO and capture lookup")
set(NCMA_WORKFLOW_CAPTURE_ID "" CACHE STRING "Explicit retained capture ID for the M8.6 real preparer tests")
set(NCMA_WORKFLOW_GIT_EXECUTABLE "" CACHE FILEPATH "Explicit installed Git for original source creation test; empty fails, no PATH fallback")
set(NCMA_WORKFLOW_SDK_INPUTS "" CACHE FILEPATH "Explicit stage-E SDK chain input declaration; missing fails, never skip")
set(NCMA_WORKFLOW_SDK_TEST_DLL "" CACHE FILEPATH "Explicit same-source extensions Services.Tests assembly; no default-harness fallback")
set(NCMA_WORKFLOW_TEST_DLL "${CMAKE_SOURCE_DIR}/managed/Ncma.Editor.Services.Tests/bin/$<IF:$<CONFIG:Debug>,Debug,Release>/net8.0/Ncma.Editor.Services.Tests.dll"
    CACHE STRING "Explicit same-source Services.Tests assembly for M8.6 entries")
foreach(_ncma_workflow_pair IN ITEMS
    "Contracts|--workflow-contract-tests"
    "Lifecycle|--workflow-job-lifecycle-tests"
    "Output|--workflow-build-output-tests"
    "Restore|--workflow-restore-inspection-tests"
    "Toolchain|--workflow-toolchain-tests"
    "Offline|--workflow-offline-preparation-tests"
    "Observation|--workflow-observation-tests"
    "ManagedClosure|--workflow-runtime-closure-tests"
    "Staging|--workflow-host-staging-tests"
    "Native|--workflow-native-preparation-tests"
    "CaptureContracts|--workflow-capture-contract-tests"
    "SourceCreationContracts|--workflow-source-capture-contract-tests"
    "SdkInputContracts|--workflow-sdk-input-contract-tests")
    string(REPLACE "|" ";" _ncma_workflow_parts "${_ncma_workflow_pair}")
    list(GET _ncma_workflow_parts 0 _ncma_workflow_name)
    list(GET _ncma_workflow_parts 1 _ncma_workflow_switch)
    add_test(NAME "NcmaWorkflowTests.${_ncma_workflow_name}" COMMAND "${DOTNET_EXECUTABLE}" "${NCMA_WORKFLOW_TEST_DLL}"
        "${NCMA_WORKFLOW_WORKSPACE_ROOT}" "${CMAKE_BINARY_DIR}/m2/plugins" "${_ncma_workflow_switch}")
    set_tests_properties("NcmaWorkflowTests.${_ncma_workflow_name}" PROPERTIES TIMEOUT 30 RUN_SERIAL TRUE
        LABELS "m8.6;workflow-new;contracts" WORKING_DIRECTORY "${NCMA_WORKFLOW_WORKSPACE_ROOT}")
endforeach()
add_test(NAME NcmaWorkflowTests.CaptureHost COMMAND "${DOTNET_EXECUTABLE}" "${NCMA_WORKFLOW_TEST_DLL}"
    "${NCMA_WORKFLOW_WORKSPACE_ROOT}" "${CMAKE_BINARY_DIR}/m2/plugins" --workflow-capture-bridge-tests "${NCMA_WORKFLOW_CAPTURE_ID}")
# Two bounded real preparer cases; no change to their internal 30-second waits.
set_tests_properties(NcmaWorkflowTests.CaptureHost PROPERTIES TIMEOUT 180 RUN_SERIAL TRUE
    LABELS "m8.6;workflow-new;real-preparer" WORKING_DIRECTORY "${NCMA_WORKFLOW_WORKSPACE_ROOT}")
add_test(NAME NcmaWorkflowTests.OriginalHost COMMAND "${DOTNET_EXECUTABLE}" "${NCMA_WORKFLOW_TEST_DLL}"
    "${NCMA_WORKFLOW_WORKSPACE_ROOT}" "${CMAKE_BINARY_DIR}/m2/plugins" --workflow-host-tests)
set_tests_properties(NcmaWorkflowTests.OriginalHost PROPERTIES TIMEOUT 180 RUN_SERIAL TRUE
    LABELS "m8.6;workflow-new;original-host" WORKING_DIRECTORY "${NCMA_WORKFLOW_WORKSPACE_ROOT}")
add_test(NAME NcmaWorkflowTests.ContinuationHost COMMAND "${DOTNET_EXECUTABLE}" "${NCMA_WORKFLOW_TEST_DLL}"
    "${NCMA_WORKFLOW_WORKSPACE_ROOT}" "${CMAKE_BINARY_DIR}/m2/plugins" --workflow-continuation-tests)
set_tests_properties(NcmaWorkflowTests.ContinuationHost PROPERTIES TIMEOUT 180 RUN_SERIAL TRUE
    LABELS "m8.6;workflow-new;continuation-host" WORKING_DIRECTORY "${NCMA_WORKFLOW_WORKSPACE_ROOT}")
add_test(NAME NcmaWorkflowTests.DomainSourceHost COMMAND "${DOTNET_EXECUTABLE}" "${NCMA_WORKFLOW_TEST_DLL}"
    "${NCMA_WORKFLOW_WORKSPACE_ROOT}" "${CMAKE_BINARY_DIR}/m2/plugins" --workflow-domain-source-tests)
set_tests_properties(NcmaWorkflowTests.DomainSourceHost PROPERTIES TIMEOUT 180 RUN_SERIAL TRUE
    LABELS "m8.6;workflow-new;domain-source-host" WORKING_DIRECTORY "${NCMA_WORKFLOW_WORKSPACE_ROOT}")
# No execution-policy bypass for new workflows. Missing host/policy/deps fail.
add_test(NAME NcmaWorkflowTests.SourceCreationHost COMMAND "${DOTNET_EXECUTABLE}" "${NCMA_WORKFLOW_TEST_DLL}"
    "${NCMA_WORKFLOW_WORKSPACE_ROOT}" "${CMAKE_BINARY_DIR}/m2/plugins" --workflow-source-capture-host-tests "${NCMA_WORKFLOW_GIT_EXECUTABLE}")
set_tests_properties(NcmaWorkflowTests.SourceCreationHost PROPERTIES TIMEOUT 180 RUN_SERIAL TRUE
    LABELS "m8.6;workflow-new;source-creation-host" WORKING_DIRECTORY "${NCMA_WORKFLOW_WORKSPACE_ROOT}")
add_test(NAME NcmaWorkflowTests.CaptureInputs COMMAND powershell.exe -NoLogo -NoProfile
    -File "${NCMA_WORKFLOW_WORKSPACE_ROOT}/scripts/Test-WorkflowBuildInputs.ps1")
set_tests_properties(NcmaWorkflowTests.CaptureInputs PROPERTIES TIMEOUT 60 RUN_SERIAL TRUE
    LABELS "m8.6;workflow-new;capture-inputs" WORKING_DIRECTORY "${NCMA_WORKFLOW_WORKSPACE_ROOT}")
add_test(NAME NcmaWorkflowTests.InventoryContracts COMMAND powershell.exe -NoLogo -NoProfile
    -File "${NCMA_WORKFLOW_WORKSPACE_ROOT}/scripts/Test-M8_6Inventory.ps1")
set_tests_properties(NcmaWorkflowTests.InventoryContracts PROPERTIES TIMEOUT 30 RUN_SERIAL TRUE
    LABELS "m8.6;workflow-new;inventory-contracts" WORKING_DIRECTORY "${NCMA_WORKFLOW_WORKSPACE_ROOT}")
# One dependent live-owner chain: capture125 + five (prepare45+SDK615) +
# six close35 + ledger/IO allowance. NOT a larger timeout on any old entry.
# Cannot split into processes without serializing live approval/borrow authority.
add_test(NAME NcmaWorkflowTests.SdkChain COMMAND "${DOTNET_EXECUTABLE}" "${NCMA_WORKFLOW_SDK_TEST_DLL}"
    "${NCMA_WORKFLOW_WORKSPACE_ROOT}" "${CMAKE_BINARY_DIR}/m2/plugins" --workflow-sdk-chain-tests "${NCMA_WORKFLOW_SDK_INPUTS}")
set_tests_properties(NcmaWorkflowTests.SdkChain PROPERTIES TIMEOUT 3900 RUN_SERIAL TRUE
    LABELS "m8.6;workflow-new;real-sdk-chain" WORKING_DIRECTORY "${NCMA_WORKFLOW_WORKSPACE_ROOT}")
