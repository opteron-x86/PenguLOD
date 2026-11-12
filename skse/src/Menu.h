#pragma once

namespace PenguLOD::Menu
{
	/// Adds PenguLOD's pages to SKSE Menu Framework (or a drop-in replacement such as ApocryphaRealm Menu
	/// Framework, which answers to the same name). Call at kDataLoaded or later: frameworks that load after
	/// PenguLOD.dll (SKSE loads plugins alphabetically) aren't there yet in SKSEPluginLoad.
	/// Does nothing, and logs it, when no menu framework is installed.
	void Register();

	/// The running game version, for the About page (from SKSEPluginLoad).
	void SetGameVersion(std::string a_version);

	/// The log file actually opened, for the About page.
	void SetLogPath(std::string a_path);
}
