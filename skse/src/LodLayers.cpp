#include "RuntimePaths.h"
#include "LodLayers.h"

#include "Adaptive.h"
#include "IniFile.h"
#include "IniParse.h"
#include "LayerNames.h"

namespace PenguLOD
{
	namespace
	{
		constexpr const char* kNames[Layers::kLayerCount] = { "Grass", "3D trees", "Tree cards" };
		constexpr const auto& kKeys = LayerNames::kKeys;
		constexpr const auto& kLevels = LayerNames::kLevels;
		static_assert(std::size(LayerNames::kKeys) == Layers::kLayerCount && std::size(LayerNames::kLevels) == Layers::kLevelCount);

		std::string KeyFor(int a_layer, int a_level) { return std::format("bShow_{}_{}", kKeys[a_layer], kLevels[a_level]); }

		bool Parse(const char* a_name, int& a_layer, int& a_level, int& a_tier) { return LayerNames::Parse(a_name, a_layer, a_level, a_tier); }

		std::string PathTo(const RE::NiAVObject* a_object)
		{
			std::string out;
			for (auto* o = a_object; o; o = o->parent) {
				const char* n = o->name.c_str();
				out = std::format("{}{}{}", (n && *n) ? n : "<unnamed>", out.empty() ? "" : " < ", out);
			}
			return out;
		}
	}

	Layers& Layers::Get()
	{
		static Layers instance;
		return instance;
	}

	const char* Layers::LayerName(int a_layer) { return kNames[a_layer]; }
	const char* Layers::LayerKey(int a_layer) { return kKeys[a_layer]; }
	int         Layers::LevelNumber(int a_level) { return kLevels[a_level]; }

	void Layers::Load()
	{
		const auto doc = Ini::Document::Read(RuntimePaths::SettingsRead());
		if (doc.Empty()) {
			return;
		}
		// Any section: older settings files used [QualityLayers].
		if (const auto* v = doc.GetAny("iGrassTiers")) {
			if (const auto n = Ini::Integer(*v)) grassTiers = static_cast<int>(std::clamp<long long>(*n, 1, kGrassTiers));
			else logger::warn("PenguLOD.ini: bad value for iGrassTiers: {}", *v);
		}
		for (int l = 0; l < kLayerCount; ++l) {
			for (int v = 0; v < kLevelCount; ++v) {
				const auto key = KeyFor(l, v);
				if (const auto* value = doc.GetAny(key)) {
					if (const auto b = Ini::Bool(*value)) show[l][v] = *b;
					else logger::warn("PenguLOD.ini: bad value for {}: {}", key, *value);
				}
			}
		}
		logger::info("Quality layers: {}", AnyHidden() ? "some layers are hidden (PenguLOD.ini [Layers])" : "everything shown");
	}

	bool Layers::Save() const
	{
		std::vector<std::pair<std::string, std::string>> values;
		for (int l = 0; l < kLayerCount; ++l) {
			for (int v = 0; v < kLevelCount; ++v) {
				values.emplace_back(KeyFor(l, v), show[l][v] ? "1" : "0");
			}
		}
		values.emplace_back("iGrassTiers", std::to_string(grassTiers));
		return IniFile::Write(RuntimePaths::Settings, "Layers", values);
	}

	bool Layers::AnyHidden() const
	{
		if (grassTiers < kGrassTiers) {
			return true;
		}
		for (const auto& row : show) {
			for (bool v : row) {
				if (!v) return true;
			}
		}
		return false;
	}

	void Layers::Preset(int a_preset)
	{
		for (auto& row : show) {
			for (auto& v : row) v = true;
		}
		grassTiers = std::clamp(kGrassTiers - a_preset, 1, kGrassTiers);  // Max 100%, High 75%, Medium 50%, Low 25% of the generated grass
		if (a_preset >= 1) {  // High: the 3D trees only where they are close
			show[kTree3D][1] = false;
			show[kTree3D][2] = false;
			show[kTree3D][3] = false;
		}
		if (a_preset >= 2) {  // Medium: grass only in the nearest two levels
			show[kGrass][2] = false;
			show[kGrass][3] = false;
		}
		if (a_preset >= 3) {  // Low: grass only at LOD4
			show[kGrass][1] = false;
		}
		Changed();
	}

	void Layers::Start()
	{
		if (_running.exchange(true)) {
			return;
		}
		std::thread([this] {
			auto ini = RuntimePaths::SettingsRead();
			std::error_code             ec;
			auto                        stamp = std::filesystem::last_write_time(ini, ec);
			int                         ticks = 0;
			for (;;) {
				std::this_thread::sleep_for(500ms);
				// Editing [Layers] in PenguLOD.ini while the game runs applies within half a second (also how it is tested).
				// A save from the menu is the plugin's own write: the values in memory are already what was saved.
				auto current = RuntimePaths::SettingsRead();
				if (const auto now = std::filesystem::last_write_time(current, ec); !ec && (current != ini || now != stamp)) {
					ini = current;
					stamp = now;
					if (!IniFile::IsOwnWrite(current, now)) {
						_reload = true;
						_dirty = true;
					}
				}
				// No layer nodes seen yet (main menu, an interior save): look again every few seconds, so the menu's checkboxes
				// appear once LOD blocks are loaded even when nothing is hidden.
				if (!_anyFound && ++ticks % 10 == 0) {
					_dirty = true;
				}
				if (!_dirty && !AnyHidden() && !_appliedHidden) {
					continue;  // everything shown and nothing to undo: no scene graph work at all
				}
				if (_queued.exchange(true)) {
					continue;
				}
				SKSE::GetTaskInterface()->AddTask([this] {
					if (_reload.exchange(false)) {
						// Read on the main thread, where show[][] is used. Adaptive quality, when it has lowered the layers, keeps its preset.
						Load();
						Adaptive::Get().Reapply();
					}
					Apply();
					_queued = false;
				});
			}
		}).detach();
	}

	void Layers::Apply()
	{
		auto* tes = RE::TES::GetSingleton();
		if (!tes || !tes->objRoot) {
			static bool noRoot = false;
			if (!noRoot) {
				noRoot = true;
				logger::info("Quality layers: no world scene yet (TES objRoot missing); will look again");
			}
			return;
		}
		_dirty = false;
		++_scans;
		const bool hidden = AnyHidden();

		// The whole scene graph is walked (LOD blocks hang somewhere under the world root; their exact parent is not
		// the same in every runtime), but never below a layer node. Hidden layers rescan every tick so blocks that
		// load later are caught within half a second; with everything shown this only runs after a change.
		RE::NiAVObject* top = tes->objRoot;
		while (top->parent) {
			top = top->parent;
		}
		// Seen in game: the loaded blocks hang under "LODRoot" (WorldRoot Node < shadow scene node < LODRoot < LandLOD).
		// Once found, only that subtree is walked, which is a small fraction of the scene. Without it, walk everything.
		if (_lodRoot && !_lodRoot->parent) {
			_lodRoot.reset();
		}
		if (!_lodRoot) {
			std::vector<std::pair<RE::NiAVObject*, int>> probe{ { top, 0 } };
			while (!probe.empty() && !_lodRoot) {
				auto [o, depth] = probe.back();
				probe.pop_back();
				if (const char* nm = o->name.c_str(); nm && _stricmp(nm, "LODRoot") == 0) {
					_lodRoot = RE::NiPointer<RE::NiNode>(o->AsNode());
					break;
				}
				if (depth < 4) {
					if (auto* node = o->AsNode()) {
						for (auto& child : node->GetChildren()) {
							if (child) probe.emplace_back(child.get(), depth + 1);
						}
					}
				}
			}
			if (_lodRoot) {
				logger::info("Quality layers: LODRoot found at {}; only that subtree is scanned from now on", PathTo(_lodRoot.get()));
			}
		}
		if (_lodRoot) {
			top = _lodRoot.get();
		}
		int                          found[kLayerCount][kLevelCount]{};
		std::size_t                  visited = 0;
		std::vector<RE::NiAVObject*> stack{ top };
		while (!stack.empty() && visited < 600000) {
			auto* o = stack.back();
			stack.pop_back();
			++visited;
			int layer = 0, level = 0, tier = 0;
			if (const char* nm = o->name.c_str(); nm && !_objLogged && _stricmp(nm, "obj") == 0) {
				// The root of a loaded .bto block is named "obj": tells where the LOD blocks hang in the scene.
				_objLogged = true;
				logger::info("Quality layers: a block root 'obj' sits at: {}", PathTo(o));
			}
			if (Parse(o->name.c_str(), layer, level, tier)) {
				if (tier == 0) {
					++found[layer][level];   // a block counts once however many tiers it was written in
				}
				const bool cull = !show[layer][level] || (layer == kGrass && tier >= grassTiers);
				if (o->GetAppCulled() != cull) {
					o->SetAppCulled(cull);
				}
				if (!_logged) {
					_logged = true;
					logger::info("Quality layers: first layer node found: {}", PathTo(o));
				}
				continue;
			}
			if (auto* node = o->AsNode()) {
				for (auto& child : node->GetChildren()) {
					if (child) stack.push_back(child.get());
				}
			}
		}

		bool changed = false;
		bool any = false;
		for (int l = 0; l < kLayerCount; ++l) {
			for (int v = 0; v < kLevelCount; ++v) {
				changed |= _found[l][v] != found[l][v];
				_found[l][v] = found[l][v];
				any |= found[l][v] > 0;
			}
		}
		_anyFound = any;
		_appliedHidden = hidden;
		if (changed || _scans <= 3) {
			logger::info("Quality layers: scan {}: scene graph walked ({} nodes). Layer nodes found: grass {}/{}/{}/{}, 3D trees {}/{}/{}/{}, tree cards {}/{}/{}/{} (LOD4/8/16/32)", _scans, visited,
				found[0][0], found[0][1], found[0][2], found[0][3], found[1][0], found[1][1], found[1][2], found[1][3], found[2][0], found[2][1], found[2][2], found[2][3]);
		}
	}
}
