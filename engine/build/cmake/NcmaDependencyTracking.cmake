include_guard(GLOBAL)

# Ninja does not include msvc_deps_prefix in its command-change detection.
# An old object can therefore retain an empty dependency record after the parser
# is corrected. Put the parser identity into every MSVC C/C++ compile command,
# including vendored targets. This unused define affects no engine/runtime API.
if(MSVC AND CMAKE_GENERATOR MATCHES "^Ninja")
    set(_ncma_dependency_identity "ncma-msvc-dependency-parser-v1")
    foreach(_ncma_prefix_name CMAKE_CL_SHOWINCLUDES_PREFIX
            CMAKE_C_CL_SHOWINCLUDES_PREFIX CMAKE_CXX_CL_SHOWINCLUDES_PREFIX)
        # Length framing distinguishes empty/unset values and embedded separators.
        if(DEFINED ${_ncma_prefix_name})
            string(LENGTH "${${_ncma_prefix_name}}" _ncma_prefix_length)
            string(APPEND _ncma_dependency_identity
                "|${_ncma_prefix_name}:${_ncma_prefix_length}:${${_ncma_prefix_name}}")
        else()
            string(APPEND _ncma_dependency_identity "|${_ncma_prefix_name}:unset")
        endif()
    endforeach()
    string(SHA256 _ncma_dependency_fingerprint "${_ncma_dependency_identity}")
    add_compile_definitions(
        "$<$<COMPILE_LANG_AND_ID:C,MSVC>:NCMA_DEPENDENCY_PARSER_${_ncma_dependency_fingerprint}=1>"
        "$<$<COMPILE_LANG_AND_ID:CXX,MSVC>:NCMA_DEPENDENCY_PARSER_${_ncma_dependency_fingerprint}=1>")
    unset(_ncma_dependency_identity)
    unset(_ncma_dependency_fingerprint)
    unset(_ncma_prefix_length)
    unset(_ncma_prefix_name)
endif()
