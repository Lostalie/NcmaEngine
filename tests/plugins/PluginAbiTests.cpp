#include "NcmaPlugin.h"
#include <windows.h>
#include <array>
#include <cassert>
#include <cstring>
#include <thread>
int main(int argc, char** argv)
{
    assert(argc == 3);
    auto library = LoadLibraryExA(argv[1], nullptr, LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR | LOAD_LIBRARY_SEARCH_SYSTEM32);
    auto foreignLibrary = LoadLibraryExA(argv[2], nullptr, LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR | LOAD_LIBRARY_SEARCH_SYSTEM32);
    assert(library && foreignLibrary);
    auto get = reinterpret_cast<NcmaGetApiV1>(GetProcAddress(library, "ncma_plugin_get_api"));
    auto foreignGet = reinterpret_cast<NcmaGetApiV1>(GetProcAddress(foreignLibrary, "ncma_plugin_get_api"));
    assert(get && foreignGet);
    NcmaErrorV1 error{}; NcmaModuleApiV1 api{}, foreign{};
    std::array<uint8_t, 56> shortOutput{}; shortOutput.fill(0xA5);
    assert(get(1, 0, shortOutput.data(), 1, &error) == NCMA_BUFFER_TOO_SMALL);
    assert(error.required_bytes == 56);
    for (auto byte : shortOutput) assert(byte == 0xA5);
    assert(get(2, 0, &api, sizeof(api), &error) == NCMA_ABI_MISMATCH);
    assert(get(1, 1, &api, sizeof(api), &error) == NCMA_ABI_MISMATCH);
    assert(get(1, 0, &api, sizeof(api), nullptr) == NCMA_INVALID_ARGUMENT);
    assert(get(1, 0, &api, sizeof(api), &error) == NCMA_OK);
    assert(foreignGet(1, 0, &foreign, sizeof(foreign), &error) == NCMA_OK);
    uint64_t a = 99, b = 0, other = 0;
    assert(api.initialize(nullptr, 1, &a, &error) == NCMA_INVALID_ARGUMENT && a == 99);
    assert(api.initialize(nullptr, 0, nullptr, &error) == NCMA_INVALID_ARGUMENT);
    assert(api.initialize(nullptr, 0, &a, &error) == NCMA_OK);
    assert(api.initialize(nullptr, 0, &b, &error) == NCMA_OK && a != b);
    assert(foreign.initialize(nullptr, 0, &other, &error) == NCMA_OK);
    NcmaModuleStatusV1 status{};
    assert(api.get_status(other, &status, &error) == NCMA_INVALID_HANDLE);
    assert(api.get_status(a, nullptr, &error) == NCMA_INVALID_ARGUMENT);
    uint32_t result = 0;
    std::thread worker([&]() { NcmaErrorV1 copy{}; NcmaModuleStatusV1 state{}; result = api.get_status(a, &state, &copy); });
    worker.join(); assert(result == NCMA_WRONG_THREAD);
    uint32_t required = 99;
    assert(api.read_diagnostic(a, nullptr, 0, &required, &error) == NCMA_OK && required == 0);
    assert(api.shutdown(a, &error) == NCMA_OK);
    assert(api.shutdown(a, &error) == NCMA_INVALID_HANDLE);
    assert(api.get_status(a, &status, &error) == NCMA_INVALID_HANDLE);
    assert(api.get_status(b, &status, &error) == NCMA_OK);
    assert(api.shutdown(b, &error) == NCMA_OK);
    assert(foreign.shutdown(other, &error) == NCMA_OK);
    FreeLibrary(foreignLibrary); FreeLibrary(library);
}
