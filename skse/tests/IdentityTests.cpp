#include "RuntimePaths.h"
#include "DataFile.h"
#include "IniFile.h"
#include <cassert>
#include <fstream>
#include <iostream>
#include <iterator>

namespace fs = std::filesystem;
using namespace PenguLOD;
static std::string Read(const fs::path& path)
{
    std::ifstream file(path);
    return {std::istreambuf_iterator<char>(file), std::istreambuf_iterator<char>()};
}
template<class T> static void Put(std::ofstream& file, const T& value)
{
    file.write(reinterpret_cast<const char*>(&value), sizeof(value));
}
static void CheckFiles(std::string prefix)
{
    for (const auto kind : {"DY", "CP", "NV"}) {
        const std::string magic = prefix + kind;
        std::ofstream file("runtime.bin", std::ios::binary);
        file.write(magic.data(), 4);
        Put(file, std::uint32_t(kind == std::string("CP") ? 1 : 2));
        Put(file, std::uint32_t(1));
        const std::string plugin = "World.esm";
        Put(file, std::uint16_t(plugin.size())); file.write(plugin.data(), plugin.size());
        Put(file, std::uint32_t(1));
        if (kind == std::string("DY")) {
            for (auto value : {0u, 123u, 0u, 456u, 0u}) Put(file, value);
            Put(file, std::array<float, 3>{1, 2, 3}); Put(file, std::array<float, 3>{0, 0, 0}); Put(file, 1.0f);
            for (auto value : {0xffffffffu, 0u, 4u}) Put(file, value);
        } else if (kind == std::string("CP")) {
            for (auto value : {0u, 456u, 0u}) Put(file, value);
            Put(file, std::array<float, 3>{1, 2, 3}); Put(file, std::array<float, 3>{0, 0, 0}); Put(file, 1.0f); Put(file, 1u);
        } else {
            for (auto value : {0u, 123u, 0u, 456u, 0xffffffffu, 0u, 4u}) Put(file, value);
            Put(file, std::array<float, 3>{1, 2, 3});
        }
        file.close();
        if (kind == std::string("DY")) {
            PenguLOD::DataFile parsed; assert(parsed.Read("runtime.bin")); assert(parsed.entries.size() == 1);
            assert(parsed.entries[0].refLocalId == 123 && parsed.entries[0].pos[2] == 3);
        } else if (kind == std::string("CP")) {
            PenguLOD::CopyFile parsed; assert(parsed.Read("runtime.bin")); assert(parsed.entries.size() == 1);
            assert(parsed.entries[0].worldLocalId == 456 && parsed.entries[0].pos[2] == 3);
        } else {
            PenguLOD::NativeFile parsed; assert(parsed.Read("runtime.bin")); assert(parsed.entries.size() == 1);
            assert(parsed.entries[0].nativeLocalId == 456 && parsed.entries[0].z == 3);
        }
    }
}
int main(int argc, char** argv)
{
    assert(argc == 2);
    fs::current_path(argv[1]);
    CheckFiles("PL"); CheckFiles("AL");
    fs::create_directories("Data/SKSE/Plugins/AnvilLOD");
    std::ofstream(RuntimePaths::LegacySettings) << "; custom settings\n[DynamicLOD]\nbEnabled=0\n[QualityLayers]\nfTree=0.7\n";
    const auto before = Read(RuntimePaths::LegacySettings);
    assert(RuntimePaths::SettingsRead() == RuntimePaths::LegacySettings);
    assert(RuntimePaths::DataFile("dyn") == fs::path("Data/SKSE/Plugins/AnvilLOD/AnvilLOD.dyn"));
    assert(IniFile::Write(RuntimePaths::Settings, "QualityLayers", {{"fTree", "0.6"}}));
    assert(Read(RuntimePaths::LegacySettings) == before);
    assert(RuntimePaths::SettingsRead() == RuntimePaths::Settings);
    const auto migrated = Read(RuntimePaths::Settings);
    assert(migrated.find("bEnabled=0") != std::string::npos);
    assert(migrated.find("fTree=0.6") != std::string::npos);
    assert(migrated.find("custom settings") != std::string::npos);
    fs::create_directories("Data/SKSE/Plugins/PenguLOD");
    // A disabled new feature must never resurrect stale data from an older generation.
    for (const auto extension : {"dyn", "copies", "native", "underside"}) {
        assert(RuntimePaths::DataFile(extension) == fs::path("Data/SKSE/Plugins/PenguLOD") / (std::string("PenguLOD.") + extension));
    }
    assert(RuntimePaths::Magic("PLDY", "PLDY", "ALDY"));
    assert(RuntimePaths::Magic("ALDY", "PLDY", "ALDY"));
    assert(!RuntimePaths::Magic("ALCP", "PLDY", "ALDY"));
    assert(RuntimePaths::Layer("PL:tree3d:4"));
    assert(RuntimePaths::Layer("AL:tree3d:4"));
    assert(!RuntimePaths::Layer("Other:tree3d:4"));
    std::cout << "Runtime identity and settings migration passed\n";
}
