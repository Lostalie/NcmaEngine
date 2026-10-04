#pragma once
#include "../contracts/NcmaPlatform.h"
struct GLFWwindow;
#if defined(NCMA_PLATFORM_EXPORTS)
#define NCMA_PLATFORM_INTERNAL __declspec(dllexport)
#else
#define NCMA_PLATFORM_INTERNAL __declspec(dllimport)
#endif
// Native-only service v1; borrows block window destruction and must release on its owner thread.
extern "C" NCMA_PLATFORM_INTERNAL uint32_t NCMA_CALL ncma_platform_borrow_window_v1(uint64_t module, uint64_t window, GLFWwindow** result, NcmaErrorV1* error) noexcept;
extern "C" NCMA_PLATFORM_INTERNAL uint32_t NCMA_CALL ncma_platform_release_window_v1(uint64_t module, uint64_t window, NcmaErrorV1* error) noexcept;
