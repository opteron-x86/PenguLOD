#pragma once
#include <filesystem>
#include <string>
#include <string_view>
#include <utility>
#include <vector>

namespace PenguLOD::IniFile
{
	/// Sets key=value pairs in one [section] of an ini file, keeping every other line (comments included).
	/// Keys are matched case-insensitively; missing keys are added at the end of the section, a missing section
	/// at the end of the file. Returns false if the file couldn't be written.
	bool Write(const std::filesystem::path& a_path, std::string_view a_section, const std::vector<std::pair<std::string, std::string>>& a_values);

	/// True when a_time is the modification time the plugin's own last Write left on a_path. File watchers use it so saving one
	/// menu page does not reload the file over values changed live on another.
	bool IsOwnWrite(const std::filesystem::path& a_path, std::filesystem::file_time_type a_time);
}
