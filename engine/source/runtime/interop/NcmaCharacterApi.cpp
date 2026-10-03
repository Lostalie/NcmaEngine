#include "interop/NcmaCharacterApi.h"
#include "assets/FbxCharacterImporter.h"

#include <stdexcept>
#include <string_view>

namespace
{
    thread_local std::string CharacterError, CharacterReply;
}

extern "C"
{
    std::uint32_t NCMA_NATIVE_CALL ncma_character_abi_version() { return 1; }
    const char* NCMA_NATIVE_CALL ncma_character_inspect_fbx(std::uint32_t version, const char* path_utf8, double sample_rate)
    {
        try
        {
            if (version != 1) throw std::invalid_argument("Unsupported character ABI version");
            if (!path_utf8 || !*path_utf8) throw std::invalid_argument("FBX source path is empty");
            NcmaEngine::Assets::FbxImportOptions options;
            options.SampleRate = sample_rate;
            const std::filesystem::path path(std::u8string_view(reinterpret_cast<const char8_t*>(path_utf8)));
            CharacterReply = NcmaEngine::Assets::FbxCharacterImporter::Import(path, options)->InspectJson();
            CharacterError.clear(); return CharacterReply.c_str();
        }
        catch (const std::exception& error) { CharacterError = error.what(); }
        catch (...) { CharacterError = "Unknown character import error"; }
        return nullptr;
    }
    const char* NCMA_NATIVE_CALL ncma_character_last_error() { return CharacterError.c_str(); }
}
