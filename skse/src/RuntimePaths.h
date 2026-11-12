#pragma once
#include <filesystem>
#include <string>
#include <string_view>

namespace PenguLOD::RuntimePaths
{
    inline const std::filesystem::path Settings{ "Data/SKSE/Plugins/PenguLOD.ini" };
    inline const std::filesystem::path LegacySettings{ "Data/SKSE/Plugins/AnvilLOD.ini" };
    // These run on the plugin's background threads too: the error_code overloads never throw (a locked or
    // unreadable folder must not take the game down).
    inline std::filesystem::path SettingsRead()
    {
        std::error_code ec;
        return std::filesystem::exists(Settings, ec) ? Settings : LegacySettings;
    }
    inline std::filesystem::path DataFile(std::string_view extension)
    {
        // Choose a whole output family. A missing new sidecar means the feature is off;
        // it must never reactivate an unrelated sidecar from an old installed output.
        std::error_code ec;
        const bool current = std::filesystem::exists("Data/SKSE/Plugins/PenguLOD", ec);
        const std::string name = current ? "PenguLOD" : "AnvilLOD";
        return std::filesystem::path("Data/SKSE/Plugins") / name / (name + "." + std::string(extension));
    }
    inline bool Magic(std::string_view value, std::string_view current, std::string_view legacy)
    {
        return value == current || value == legacy;
    }
    inline bool Layer(std::string_view name)
    {
        return name.starts_with("PL:") || name.starts_with("AL:");
    }
}
