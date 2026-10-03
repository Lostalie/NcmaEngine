#pragma once

#include <algorithm>
#include <cstdint>

namespace NcmaEngine::Rendering
{
    enum class ShadowFilter : std::uint8_t
    {
        Pcf3x3,
        Pcf5x5,
        Pcss
    };

    [[nodiscard]] constexpr std::uint32_t PcfKernelRadius(ShadowFilter filter) noexcept
    {
        return filter == ShadowFilter::Pcf5x5 ? 2U : 1U;
    }

    [[nodiscard]] constexpr std::uint32_t PcfSampleCount(ShadowFilter filter) noexcept
    {
        const std::uint32_t diameter = PcfKernelRadius(filter) * 2U + 1U;
        return diameter * diameter;
    }

    [[nodiscard]] constexpr std::uint32_t ShadowSampleCount(ShadowFilter filter) noexcept
    {
        return filter == ShadowFilter::Pcss ? 50U : PcfSampleCount(filter);
    }

    struct PbrMaterialParameters final
    {
        float BaseColor[4] = {1.0F, 1.0F, 1.0F, 1.0F};
        float Metallic = 0.0F;
        float Roughness = 0.5F;
        float NormalScale = 1.0F;
        float OcclusionStrength = 1.0F;
        float Emissive[3] = {0.0F, 0.0F, 0.0F};

        void Sanitize()
        {
            Metallic = std::clamp(Metallic, 0.0F, 1.0F);
            Roughness = std::clamp(Roughness, 0.04F, 1.0F);
            NormalScale = std::max(0.0F, NormalScale);
            OcclusionStrength = std::clamp(OcclusionStrength, 0.0F, 1.0F);
        }
    };

    struct DirectionalShadowSettings final
    {
        bool Enabled = true;
        std::uint32_t Resolution = 2048;
        std::uint32_t CascadeCount = 4;
        float MaxDistance = 100.0F;
        float CascadeLambda = 0.75F;
        float ConstantBias = 0.0005F;
        float SlopeBias = 1.5F;
        float LightRadius = 0.05F;
        ShadowFilter Filter = ShadowFilter::Pcf5x5;

        void Sanitize()
        {
            Resolution = std::clamp(Resolution, 256U, 8192U);
            CascadeCount = std::clamp(CascadeCount, 1U, 4U);
            MaxDistance = std::max(MaxDistance, 1.0F);
            CascadeLambda = std::clamp(CascadeLambda, 0.0F, 1.0F);
            LightRadius = std::clamp(LightRadius, 0.0F, 0.25F);
        }
    };

    struct ContactShadowSettings final
    {
        bool Enabled = true;
        std::uint32_t StepCount = 16;
        float MaxDistance = 0.8F;
        float Thickness = 0.08F;
        float Strength = 0.65F;

        void Sanitize()
        {
            StepCount = std::clamp(StepCount, 4U, 32U);
            MaxDistance = std::clamp(MaxDistance, 0.05F, 5.0F);
            Thickness = std::clamp(Thickness, 0.001F, 0.5F);
            Strength = std::clamp(Strength, 0.0F, 1.0F);
        }
    };
}
