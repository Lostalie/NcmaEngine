#pragma once

// Test-only forced include, before any project/system headers. Production retains
// its normal NDEBUG configuration; assertions must execute in both test builds.
#ifdef NDEBUG
#undef NDEBUG
#endif

#include <cassert>
