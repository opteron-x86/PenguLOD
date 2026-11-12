#include "RuntimePaths.h"
#include "LodDistances.h"
#include "IniFile.h"
#include "IniParse.h"

#include <Windows.h>

namespace PenguLOD
{
	namespace
	{
		const std::filesystem::path kDynDefaults{ "Data/MCM/Config/DynDOLOD/settings.ini" };
		const std::filesystem::path kDynUser{ "Data/MCM/Settings/DynDOLOD.ini" };

		// key in the files -> candidate game settings ("name:Section"), first one that exists is used
		const std::vector<std::pair<std::string, std::vector<std::string>>> kKeys{
			{ "fBlockLevel0Distance", { "fBlockLevel0Distance:TerrainManager" } },
			{ "fBlockLevel1Distance", { "fBlockLevel1Distance:TerrainManager" } },
			{ "fBlockMaximumDistance", { "fBlockMaximumDistance:TerrainManager" } },
			{ "fSplitDistanceMult", { "fSplitDistanceMult:TerrainManager" } },
			{ "fTreeLoadDistance", { "fTreeLoadDistance:TerrainManager" } },
			{ "fSkyCellRefFadeDistance", { "fSkyCellRefFadeDistance:General", "fSkyCellRefFadeDistance:LOD", "fSkyCellRefFadeDistance:TerrainManager" } },
			// Full-model fade distances (SkyrimPrefs [LOD]); the game's own "Object/Item/Actor fade" sliders
			{ "fLODFadeOutMultObjects", { "fLODFadeOutMultObjects:LOD" } },
			{ "fLODFadeOutMultItems", { "fLODFadeOutMultItems:LOD" } },
			{ "fLODFadeOutMultActors", { "fLODFadeOutMultActors:LOD" } },
			{ "fLODFadeOutMultSkyCell", { "fLODFadeOutMultSkyCell:LOD" } },
			// Real grass (SkyrimPrefs/Skyrim.ini [Grass]); grass LOD takes over where it fades out
			{ "fGrassStartFadeDistance", { "fGrassStartFadeDistance:Grass" } },
			{ "fGrassMaxStartFadeDistance", { "fGrassMaxStartFadeDistance:Grass" } },
			{ "fGrassMinStartFadeDistance", { "fGrassMinStartFadeDistance:Grass" } },
			{ "fGrassFadeRange", { "fGrassFadeRange:Grass" } },
		};

		RE::Setting* FindSetting(const std::string& a_name)
		{
			if (auto ini = RE::INISettingCollection::GetSingleton()) {
				if (auto s = ini->GetSetting(a_name)) return s;
			}
			if (auto prefs = RE::INIPrefSettingCollection::GetSingleton()) {
				if (auto s = prefs->GetSetting(a_name)) return s;
			}
			return nullptr;
		}

		bool DynDolodDllLoaded()
		{
			// The loaded module, not the file: a DLL SKSE refused to load (built for another game version) manages nothing.
			return ::GetModuleHandleW(L"DynDOLOD.dll") != nullptr;
		}

		bool Flag(const Ini::Document& a_doc, const char* a_key, bool a_default)
		{
			const auto* v = a_doc.Get("LOD", a_key);
			if (!v) return a_default;
			if (const auto b = Ini::Bool(*v)) return *b;
			logger::warn("PenguLOD.ini [LOD]: bad value for {}: {}", a_key, *v);
			return a_default;
		}
	}

	bool LodDistances::PluginInstalled(const char* a_dll)
	{
		std::error_code ec;
		return std::filesystem::exists(std::filesystem::path("Data/SKSE/Plugins") / a_dll, ec);
	}

	LodDistances& LodDistances::Get()
	{
		static LodDistances instance;
		return instance;
	}

	std::map<std::string, float> LodDistances::Read() const
	{
		// Later files win: DynDOLOD DLL NG's defaults, the values the DynDOLOD MCM saved, then PenguLOD.ini [LOD].
		const Ini::Document files[]{ Ini::Document::Read(kDynDefaults), Ini::Document::Read(kDynUser), Ini::Document::Read(RuntimePaths::SettingsRead()) };
		const char*         sections[]{ "Settings", "Settings", "LOD" };
		std::map<std::string, float> values;
		for (const auto& [key, candidates] : kKeys) {
			for (int f = 0; f < 3; ++f) {
				const auto* v = files[f].Get(sections[f], key);
				if (!v || v->empty()) continue;
				if (const auto n = Ini::Float(*v)) values[key] = *n;
				else logger::warn("LOD settings: bad value for {}: {}", key, *v);
			}
		}
		return values;
	}

	std::filesystem::file_time_type LodDistances::NewestWrite() const
	{
		std::filesystem::file_time_type newest{};
		const auto currentSettings = RuntimePaths::SettingsRead();
		for (const auto* p : { &kDynDefaults, &kDynUser, &currentSettings }) {
			std::error_code ec;
			const auto t = std::filesystem::last_write_time(*p, ec);
			if (!ec && t > newest) newest = t;
		}
		return newest;
	}

	void LodDistances::Apply(const char* a_reason)
	{
		const auto settings = Ini::Document::Read(RuntimePaths::SettingsRead());
		_lastWrite = NewestWrite();
		_deferring = DynDolodDllLoaded() && !Flag(settings, "bOverrideDynDOLOD", false);
		if (!Flag(settings, "bApplyLodDistances", true)) return;
		if (_deferring) {
			logger::info("LOD settings ({}): DynDOLOD.dll is loaded and manages them; not applying (set bOverrideDynDOLOD=1 to take over)", a_reason);
			return;
		}

		const auto values = Read();
		if (values.empty()) return;
		for (const auto& [key, candidates] : kKeys) {
			auto it = values.find(key);
			if (it == values.end()) continue;
			RE::Setting* setting = nullptr;
			for (const auto& c : candidates) {
				if ((setting = FindSetting(c))) break;
			}
			if (!setting || setting->GetType() != RE::Setting::Type::kFloat) {
				logger::warn("LOD settings: game setting for {} not found", key);
				continue;
			}
			const float old = setting->data.f;
			setting->data.f = it->second;
			if (old != it->second) logger::info("LOD settings ({}): {} {} -> {}", a_reason, setting->GetName(), old, it->second);
		}
	}

	void LodDistances::StartWatching()
	{
		if (_watching.exchange(true)) return;
		std::thread([this] {
			for (;;) {
				std::this_thread::sleep_for(std::chrono::seconds(2));
				const auto newest = NewestWrite();
				if (newest == _lastWrite.load()) continue;
				// Saving another menu page rewrites PenguLOD.ini: that is not an edit to apply over the live sliders.
				if (IniFile::IsOwnWrite(RuntimePaths::Settings, newest)) {
					_lastWrite = newest;
					continue;
				}
				SKSE::GetTaskInterface()->AddTask([this] { Apply("file changed"); });
				std::this_thread::sleep_for(std::chrono::seconds(1));  // let the task run before checking again
			}
		}).detach();
	}

	const std::vector<LodDistances::Entry>& LodDistances::Entries()
	{
		static const std::vector<Entry> entries{
			{ "LOD distances", "fBlockLevel0Distance", 5000.0f, 200000.0f, "%.0f", "Where LOD4 blocks switch to LOD8" },
			{ "LOD distances", "fBlockLevel1Distance", 10000.0f, 400000.0f, "%.0f", "Where LOD8 blocks switch to LOD16" },
			{ "LOD distances", "fBlockMaximumDistance", 50000.0f, 800000.0f, "%.0f", "Furthest object/terrain LOD is drawn" },
			{ "LOD distances", "fSplitDistanceMult", 0.25f, 4.0f, "%.2f", "Scales when blocks split into the next level" },
			{ "LOD distances", "fTreeLoadDistance", 10000.0f, 400000.0f, "%.0f", "How far tree LOD is drawn" },
			{ "LOD distances", "fSkyCellRefFadeDistance", 10000.0f, 800000.0f, "%.0f", "Fade distance of large sky-cell references" },
			{ "Full model fade", "fLODFadeOutMultObjects", 1.0f, 40.0f, "%.1f", "Objects (the game's Object Fade slider)" },
			{ "Full model fade", "fLODFadeOutMultItems", 1.0f, 40.0f, "%.1f", "Items (Item Fade)" },
			{ "Full model fade", "fLODFadeOutMultActors", 1.0f, 40.0f, "%.1f", "Actors (Actor Fade)" },
			{ "Full model fade", "fLODFadeOutMultSkyCell", 1.0f, 40.0f, "%.1f", "Sky-cell (large) references" },
			{ "Grass", "fGrassStartFadeDistance", 0.0f, 1000000.0f, "%.0f", "Where real grass starts to fade (the game's Grass Fade slider). Ctrl+click to type a value; 999999 is a common setting" },
			{ "Grass", "fGrassMaxStartFadeDistance", 0.0f, 100000.0f, "%.0f", "Upper limit of the start fade" },
			{ "Grass", "fGrassMinStartFadeDistance", 0.0f, 50000.0f, "%.0f", "Lower limit of the start fade" },
			{ "Grass", "fGrassFadeRange", 0.0f, 100000.0f, "%.0f", "How long the fade is; grass LOD shows beyond it" },
		};
		return entries;
	}

	namespace
	{
		RE::Setting* SettingFor(const std::string& a_key)
		{
			for (const auto& [key, candidates] : kKeys) {
				if (Ini::Lower(key) != Ini::Lower(a_key)) continue;
				for (const auto& c : candidates)
					if (auto s = FindSetting(c); s && s->GetType() == RE::Setting::Type::kFloat) return s;
			}
			return nullptr;
		}
	}

	std::optional<float> LodDistances::GetGame(const std::string& a_key) const
	{
		if (auto s = SettingFor(a_key)) return s->data.f;
		return std::nullopt;
	}

	bool LodDistances::SetGame(const std::string& a_key, float a_value)
	{
		auto s = SettingFor(a_key);
		if (!s) return false;
		s->data.f = a_value;
		return true;
	}

	bool LodDistances::SaveToIni()
	{
		std::vector<std::pair<std::string, std::string>> values{ { "bApplyLodDistances", "1" } };
		for (const auto& e : Entries()) {
			if (auto v = GetGame(e.key)) values.emplace_back(e.key, std::format("{:.2f}", *v));
		}
		const bool ok = IniFile::Write(RuntimePaths::Settings, "LOD", values);
		_lastWrite = NewestWrite();  // our own save is not a change to re-apply
		logger::info("LOD settings saved to {}: {}", RuntimePaths::Settings.string(), ok ? "ok" : "FAILED");
		return ok;
	}

	bool LodDistances::DeferringToDynDolod() const
	{
		return _deferring;  // set by Apply; the menu asks every frame
	}
}
