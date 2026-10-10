#pragma once
#include "../contracts/NcmaEnvironmentCook.h"
#include <vector>
namespace NcmaEngine::Rendering::EnvironmentCook {
struct Layout { uint32_t floats=0,levels=0;uint64_t work=0; };
bool Describe(const NcmaEnvironmentCookV1&,Layout&) noexcept;
bool ValidInput(const std::vector<float>&) noexcept;
std::vector<float> Run(const NcmaEnvironmentCookV1&,const std::vector<float>&,const Layout&);
}
