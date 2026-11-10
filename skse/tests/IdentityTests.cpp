#include "RuntimePaths.h"
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
int main(int argc, char** argv)
{
    assert(argc == 2);
    fs::current_path(argv[1]);
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
