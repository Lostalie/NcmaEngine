# Package IO belongs to the canonical checkout; compiled/native inputs may be
# captured copies. Empty binding is only the ordinary in-checkout mode.
set(NCMA_PACKAGE_VERIFICATION_WORKSPACE_ROOT "${CMAKE_SOURCE_DIR}" CACHE PATH "Canonical package/receipt/test IO workspace")
set(NCMA_PACKAGE_VERIFICATION_CAPTURE_ROOT "" CACHE PATH "Exact captured source generation for package verification")
set(NCMA_PACKAGE_VERIFICATION_MANIFEST_SHA256 "" CACHE STRING "Explicit capture manifest SHA256, never inferred as approval")
set(_ncma_package_binding_args)
if(NOT NCMA_PACKAGE_VERIFICATION_WORKSPACE_ROOT STREQUAL CMAKE_SOURCE_DIR OR
   NCMA_PACKAGE_VERIFICATION_CAPTURE_ROOT OR NCMA_PACKAGE_VERIFICATION_MANIFEST_SHA256)
    if(NOT IS_ABSOLUTE "${NCMA_PACKAGE_VERIFICATION_WORKSPACE_ROOT}" OR
       NOT IS_ABSOLUTE "${NCMA_PACKAGE_VERIFICATION_CAPTURE_ROOT}" OR
       NOT IS_DIRECTORY "${NCMA_PACKAGE_VERIFICATION_WORKSPACE_ROOT}" OR
       NOT NCMA_PACKAGE_VERIFICATION_MANIFEST_SHA256 MATCHES "^[0-9A-F]+$")
        message(FATAL_ERROR "Explicit package verification workspace/capture/manifest binding required")
    endif()
    string(LENGTH "${NCMA_PACKAGE_VERIFICATION_MANIFEST_SHA256}" _ncma_package_hash_length)
    if(NOT _ncma_package_hash_length EQUAL 64)
        message(FATAL_ERROR "Package verification manifest SHA256 must be 64 uppercase hex characters")
    endif()
    get_filename_component(_ncma_package_capture_source "${NCMA_PACKAGE_VERIFICATION_CAPTURE_ROOT}/source" REALPATH)
    get_filename_component(_ncma_package_compiled_source "${CMAKE_SOURCE_DIR}" REALPATH)
    if(NOT _ncma_package_capture_source STREQUAL _ncma_package_compiled_source)
        message(FATAL_ERROR "Package verification capture must own this CMake source tree")
    endif()
    file(SHA256 "${NCMA_PACKAGE_VERIFICATION_CAPTURE_ROOT}/capture.json" _ncma_package_manifest_hash)
    string(TOUPPER "${_ncma_package_manifest_hash}" _ncma_package_manifest_hash)
    if(NOT _ncma_package_manifest_hash STREQUAL NCMA_PACKAGE_VERIFICATION_MANIFEST_SHA256)
        message(FATAL_ERROR "Package verification capture manifest changed")
    endif()
    list(APPEND _ncma_package_binding_args
        -VerificationWorkspace "${NCMA_PACKAGE_VERIFICATION_WORKSPACE_ROOT}"
        -CapturedBuildRoot "${NCMA_PACKAGE_VERIFICATION_CAPTURE_ROOT}"
        -ExpectedCaptureManifestSha256 "${NCMA_PACKAGE_VERIFICATION_MANIFEST_SHA256}")
endif()
# The original receipt validator requires this exact configuration-specific root.
set(_ncma_package_probe_dll "${NCMA_PACKAGE_VERIFICATION_WORKSPACE_ROOT}/out/build/windows-ninja-$<IF:$<CONFIG:Debug>,debug,release>/m8-4-ai/bin/Ncma.Editor.Services.Tests/$<IF:$<CONFIG:Debug>,debug,release>/Ncma.Editor.Services.Tests.dll")

# Independent Python inputs have their OWN capture/receipt, not the engine source stamp.
# Empty ordinary developer binding preserves the existing no-Python product check.
# Captured worker tests reject an absent preparation at runtime (never skip/pass).
set(NCMA_PYTHON_VERIFICATION_ENVIRONMENT "" CACHE PATH "Exact prepared Python environment generation")
set(NCMA_PYTHON_VERIFICATION_RECEIPT_SHA256 "" CACHE STRING "Explicit Python environment receipt SHA256")
set(_ncma_python_runtime_args)
if(NCMA_PYTHON_VERIFICATION_ENVIRONMENT OR NCMA_PYTHON_VERIFICATION_RECEIPT_SHA256)
    string(LENGTH "${NCMA_PYTHON_VERIFICATION_RECEIPT_SHA256}" _ncma_python_hash_length)
    if(NOT IS_ABSOLUTE "${NCMA_PYTHON_VERIFICATION_ENVIRONMENT}" OR
       NOT NCMA_PYTHON_VERIFICATION_RECEIPT_SHA256 MATCHES "^[0-9A-F]+$" OR NOT _ncma_python_hash_length EQUAL 64)
        message(FATAL_ERROR "Explicit Python environment root and 64-character receipt SHA256 required")
    endif()
    file(SHA256 "${NCMA_PYTHON_VERIFICATION_ENVIRONMENT}/receipt.json" _ncma_python_receipt_hash)
    string(TOUPPER "${_ncma_python_receipt_hash}" _ncma_python_receipt_hash)
    if(NOT _ncma_python_receipt_hash STREQUAL NCMA_PYTHON_VERIFICATION_RECEIPT_SHA256)
        message(FATAL_ERROR "Prepared Python receipt changed")
    endif()
    list(APPEND _ncma_python_runtime_args --python-environment
        "${NCMA_PYTHON_VERIFICATION_ENVIRONMENT}" "${NCMA_PYTHON_VERIFICATION_RECEIPT_SHA256}")
endif()
