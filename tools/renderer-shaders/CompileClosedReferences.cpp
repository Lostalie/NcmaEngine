// Build-host only: no device, project shader, includes, macros or runtime fallback.
#include "renderer/ClosedShaderDefinitions.h"
#include <wrl/client.h>
#include <filesystem>
#include <fstream>
#include <iostream>
#include <sstream>
#include <vector>

int wmain(int argc, wchar_t** argv) {
    try {
        if (argc != 2) throw std::runtime_error("Usage: NcmaClosedShaderCompiler <output-header>");
        using namespace NcmaEngine::Rendering::ClosedShaderDefinitions;
        std::ostringstream header;
        header << "// Generated closed reference DXBC; edit shared source definitions, not this file.\n"
            "#pragma once\n#include <cstddef>\n#include <string_view>\n"
            "namespace NcmaEngine::Rendering::ClosedShaderBytes {\n"
            "inline constexpr unsigned Flags=" << Flags << "u;\n";
        const auto& definitions = All();
        std::vector<Microsoft::WRL::ComPtr<ID3DBlob>> compiled;
        for (size_t i = 0; i < definitions.size(); ++i) {
            const auto& d = definitions[i];
            if (d.source.empty() || d.source.size() > 262144 || d.source.find(")ncma\"") != std::string_view::npos)
                throw std::runtime_error("Invalid bounded reference source");
            for (size_t j = 0; j < i; ++j) {
                const auto& other = definitions[j];
                if (d.source == other.source && std::string_view(d.entry) == other.entry &&
                    std::string_view(d.target) == other.target && std::string_view(d.sourceName) == other.sourceName)
                    throw std::runtime_error("Duplicate closed reference key");
            }
            Microsoft::WRL::ComPtr<ID3DBlob> code, errors;
            const auto hr = D3DCompile(d.source.data(), d.source.size(), d.sourceName, nullptr, nullptr,
                d.entry, d.target, Flags, 0, &code, &errors);
            if (errors && errors->GetBufferSize()) {
                std::cerr << d.entry << ": " << static_cast<const char*>(errors->GetBufferPointer()) << '\n';
                throw std::runtime_error("Reference shader diagnostics are fatal");
            }
            if (FAILED(hr) || !code || !code->GetBufferSize() || code->GetBufferSize() > 1048576)
                throw std::runtime_error("Reference shader compilation failed");
            compiled.push_back(code);
            header << "inline constexpr unsigned char Code" << i << "[]={\n";
            const auto* bytes = static_cast<const unsigned char*>(code->GetBufferPointer());
            for (size_t k = 0; k < code->GetBufferSize(); ++k) {
                header << static_cast<unsigned>(bytes[k]) << ',';
                if (k % 24 == 23) header << '\n';
            }
            header << "\n};\n";
        }
        header << "struct Entry {std::string_view source,entry,target,sourceName;const unsigned char* bytes;size_t size;};\n"
            "inline constexpr Entry Entries[]={\n";
        for (size_t i = 0; i < definitions.size(); ++i) {
            const auto& d = definitions[i];
            header << "{R\"ncma(" << d.source << ")ncma\",\"" << d.entry << "\",\"" << d.target <<
                "\",\"" << d.sourceName << "\",Code" << i << ",sizeof(Code" << i << ")},\n";
        }
        header << "};\n}\n";
        // All 16 compiles must succeed before opening any prior output.
        // A failed write fails the build; products must never consume stale success.
        const std::filesystem::path outputPath(argv[1]);
        std::ofstream output(outputPath, std::ios::binary | std::ios::trunc);
        if (!output) throw std::runtime_error("Cannot create reference header");
        output << header.str();
        output.close();
        if (!output) throw std::runtime_error("Cannot finish reference header");
        std::cout << "Compiled 16 exact closed references; no runtime compilation fallback\n";
        return 0;
    } catch (const std::exception& e) {
        std::cerr << e.what() << '\n';
        return 1;
    }
}
