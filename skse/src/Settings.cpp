#include "RuntimePaths.h"
#include "Settings.h"
#include "IniFile.h"
#include "IniParse.h"

#include <algorithm>
#include <limits>

namespace PenguLOD
{
	Settings& Settings::Get()
	{
		static Settings instance;
		return instance;
	}

	namespace
	{
		/// Typed reads with range clamps. A value that isn't of the key's type is logged and the default kept.
		struct Reader
		{
			const Ini::Document& doc;

			const std::string* Value(const char* a_key) const { return doc.GetAny(a_key); }
			void               Bad(const char* a_key, const std::string& a_value) const { logger::warn("PenguLOD.ini: bad value for {}: {}", a_key, a_value); }

			void Bool(const char* a_key, bool& a_out) const
			{
				if (const auto* v = Value(a_key)) {
					if (const auto b = Ini::Bool(*v)) a_out = *b;
					else Bad(a_key, *v);
				}
			}
			void Float(const char* a_key, float& a_out, float a_min = std::numeric_limits<float>::lowest(), float a_max = std::numeric_limits<float>::max()) const
			{
				if (const auto* v = Value(a_key)) {
					if (const auto f = Ini::Float(*v)) a_out = std::clamp(*f, a_min, a_max);
					else Bad(a_key, *v);
				}
			}
			template <class T>
			void Int(const char* a_key, T& a_out, long long a_min, long long a_max) const
			{
				if (const auto* v = Value(a_key)) {
					if (const auto n = Ini::Integer(*v)) a_out = static_cast<T>(std::clamp(*n, a_min, a_max));
					else Bad(a_key, *v);
				}
			}
		};
	}

	void Settings::Load()
	{
		const auto path = RuntimePaths::SettingsRead();
		const auto doc = Ini::Document::Read(path);
		if (doc.Empty()) {
			logger::info("No {} - using defaults", path.string());
			return;
		}
		// Keys are read from any section: older settings files used [DynamicLOD] for what is now [Dynamic].
		const Reader r{ doc };
		r.Bool("bEnabled", enabled);
		r.Float("fMaxDistance", maxDistance, 0.0f, 10000000.0f);
		r.Int("iUpdateIntervalMs", updateIntervalMs, 100, 60000);
		r.Int("iMaxShown", maxShown, 0, 1000000);
		r.Int("iMaxShowPerUpdate", maxShowPerUpdate, 1, 1000000);
		r.Bool("bGridObjects", gridObjects);
		r.Bool("bWaterPlanes", waterPlanes);
		r.Float("fNearGridDistance", nearGridDistance, 0.0f, 10000000.0f);
		r.Float("fFarGridDistance", farGridDistance, 0.0f, 10000000.0f);
		r.Float("fEffectsDistance", effectsDistance, 4096.0f, 200000.0f);
		r.Bool("bDriveSequences", driveSequences);
		r.Bool("bTickEngineManagers", tickManagers);
		r.Bool("bAnimateExperimental", animate);
		r.Int("iAnimationFps", animationFps, 0, 120);
		r.Bool("bAnimateWater", animateWater);
		r.Float("fWaterScrollSpeed", waterScrollSpeed, 0.0f, 0.5f);
		r.Float("fWaterOpacity", waterOpacity, 0.05f, 2.0f);
		r.Bool("bAdaptiveQuality", adaptiveQuality);
		r.Int("iAdaptiveFloor", adaptiveFloor, 0, 3);
		r.Int("iAdaptiveTargetFps", adaptiveTargetFps, 20, 144);
		r.Bool("bCityGrass", cityGrass);
		r.Bool("bUndersides", undersides);
		r.Bool("bGlowWindows", glowWindows);
		r.Float("fGlowIntensity", glowIntensity, 0.0f, 10.0f);
		r.Float("fGlowDuskStart", glowDuskStart, 0.0f, 24.0f);
		r.Float("fGlowDuskEnd", glowDuskEnd, 0.0f, 24.0f);
		r.Float("fGlowDawnStart", glowDawnStart, 0.0f, 24.0f);
		r.Float("fGlowDawnEnd", glowDawnEnd, 0.0f, 24.0f);
		r.Bool("bParentCopies", parentCopies);
		r.Int("iParentCopyExtraCells", parentCopyExtraCells, 0, 6);
		r.Bool("bParentCopyAllCells", parentCopyAllCells);
		r.Bool("bVerboseLog", verboseLog);
		logger::info("Settings: enabled={} maxDistance={} interval={}ms maxShown={} perUpdate={} gridObjects={} water={} near={} far={} animate={} animationFps={}",
			enabled, maxDistance, updateIntervalMs, maxShown, maxShowPerUpdate, gridObjects, waterPlanes, nearGridDistance, farGridDistance, animate, animationFps);
		logger::info("Settings: parentCopies={} parentCopyAllCells={} glowWindows={} glowIntensity={}", parentCopies, parentCopyAllCells, glowWindows, glowIntensity);
	}

	bool Settings::Save() const
	{
		return IniFile::Write(RuntimePaths::Settings, "Dynamic", {
			{ "bEnabled", enabled ? "1" : "0" },
			{ "fMaxDistance", std::format("{:.0f}", maxDistance) },
			{ "iUpdateIntervalMs", std::to_string(updateIntervalMs) },
			{ "iMaxShown", std::to_string(maxShown) },
			{ "iMaxShowPerUpdate", std::to_string(maxShowPerUpdate) },
			{ "bGridObjects", gridObjects ? "1" : "0" },
			{ "bWaterPlanes", waterPlanes ? "1" : "0" },
			{ "fNearGridDistance", std::format("{:.0f}", nearGridDistance) },
			{ "fFarGridDistance", std::format("{:.0f}", farGridDistance) },
			{ "fEffectsDistance", std::format("{:.0f}", effectsDistance) },
			{ "bAnimateExperimental", animate ? "1" : "0" },
			{ "bTickEngineManagers", tickManagers ? "1" : "0" },
			{ "bDriveSequences", driveSequences ? "1" : "0" },
			{ "iAnimationFps", std::to_string(animationFps) },
			{ "bAnimateWater", animateWater ? "1" : "0" },
			{ "fWaterScrollSpeed", std::format("{:.3f}", waterScrollSpeed) },
			{ "fWaterOpacity", std::format("{:.2f}", waterOpacity) },
			{ "bAdaptiveQuality", adaptiveQuality ? "1" : "0" },
			{ "iAdaptiveTargetFps", std::to_string(adaptiveTargetFps) },
			{ "iAdaptiveFloor", std::to_string(adaptiveFloor) },
			{ "bCityGrass", cityGrass ? "1" : "0" },
			{ "bUndersides", undersides ? "1" : "0" },
			{ "bGlowWindows", glowWindows ? "1" : "0" },
			{ "fGlowIntensity", std::format("{:.2f}", glowIntensity) },
			{ "fGlowDuskStart", std::format("{:.1f}", glowDuskStart) },
			{ "fGlowDuskEnd", std::format("{:.1f}", glowDuskEnd) },
			{ "fGlowDawnStart", std::format("{:.1f}", glowDawnStart) },
			{ "fGlowDawnEnd", std::format("{:.1f}", glowDawnEnd) },
			{ "bParentCopies", parentCopies ? "1" : "0" },
			{ "bParentCopyAllCells", parentCopyAllCells ? "1" : "0" },
			{ "iParentCopyExtraCells", std::to_string(parentCopyExtraCells) },
			{ "bVerboseLog", verboseLog ? "1" : "0" },
		});
	}
}
