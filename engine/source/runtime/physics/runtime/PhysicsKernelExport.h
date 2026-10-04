#pragma once
// Transitional C++ callers only. New clients use contracts/NcmaPhysics.h.
#if defined(_WIN32)
#if defined(NCMA_PHYSICS_EXPORTS)
#define NCMA_PHYSICS_KERNEL __declspec(dllexport)
#else
#define NCMA_PHYSICS_KERNEL __declspec(dllimport)
#endif
#else
#define NCMA_PHYSICS_KERNEL
#endif
