#include "scene/SceneUuid.h"

#include <array>
#include <charconv>
#include <format>
#include <mutex>
#include <random>

namespace NcmaEngine
{
    namespace
    {
        std::mt19937_64& Generator()
        {
            static std::mt19937_64 generator([] {
                std::random_device device;
                std::array<std::uint32_t, 8> seedData{};
                for (std::uint32_t& value : seedData)
                    value = device();
                std::seed_seq seed(seedData.begin(), seedData.end());
                return std::mt19937_64(seed);
            }());
            return generator;
        }

        std::mutex& GeneratorMutex()
        {
            static std::mutex mutex;
            return mutex;
        }

        bool ParseHex(std::string_view text, std::uint64_t& value)
        {
            value = 0;
            const auto [end, error] = std::from_chars(text.data(), text.data() + text.size(), value, 16);
            return error == std::errc{} && end == text.data() + text.size();
        }
    }

    SceneUuid SceneUuid::New()
    {
        std::scoped_lock lock(GeneratorMutex());
        SceneUuid result{Generator()(), Generator()()};
        // RFC 4122-compatible version/variant bits make exported identifiers conventional.
        result.High = (result.High & ~0xF000ULL) | 0x4000ULL;
        result.Low = (result.Low & 0x3FFFFFFFFFFFFFFFULL) | 0x8000000000000000ULL;
        if (!result.IsValid())
            result.Low = 1;
        return result;
    }

    std::string SceneUuid::ToString() const
    {
        return std::format(
            "{:08x}-{:04x}-{:04x}-{:04x}-{:012x}",
            static_cast<std::uint32_t>(High >> 32U),
            static_cast<std::uint16_t>((High >> 16U) & 0xFFFFU),
            static_cast<std::uint16_t>(High & 0xFFFFU),
            static_cast<std::uint16_t>(Low >> 48U),
            Low & 0x0000FFFFFFFFFFFFULL);
    }

    std::optional<SceneUuid> SceneUuid::Parse(std::string_view value)
    {
        if (value.size() != 36 || value[8] != '-' || value[13] != '-' ||
            value[18] != '-' || value[23] != '-')
            return std::nullopt;

        std::array<char, 32> compact{};
        std::size_t destination = 0;
        for (const char character : value)
            if (character != '-')
                compact[destination++] = character;

        std::uint64_t high = 0;
        std::uint64_t low = 0;
        if (!ParseHex(std::string_view(compact.data(), 16), high) ||
            !ParseHex(std::string_view(compact.data() + 16, 16), low))
            return std::nullopt;
        SceneUuid result{high, low};
        return result.IsValid() ? std::optional(result) : std::nullopt;
    }
}
