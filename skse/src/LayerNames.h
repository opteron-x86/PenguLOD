#pragma once
#include <string>
#include <string_view>

#include "RuntimePaths.h"

namespace PenguLOD::LayerNames
{
	inline constexpr const char* kKeys[3] = { "grass", "tree3d", "treecard" };
	inline constexpr int         kLevels[4] = { 4, 8, 16, 32 };

	/// "PL:grass:16" -> layer 0, level index 2, tier 0; "PL:grass:16:2" -> the same with tier 2. False for anything else.
	inline bool Parse(const char* a_name, int& a_layer, int& a_level, int& a_tier)
	{
		if (!a_name || !RuntimePaths::Layer(a_name)) {
			return false;
		}
		const std::string_view rest{ a_name + 3 };
		const auto             colon = rest.find(':');
		if (colon == std::string_view::npos) {
			return false;
		}
		a_layer = -1;
		for (int i = 0; i < 3; ++i) {
			if (rest.substr(0, colon) == kKeys[i]) a_layer = i;
		}
		a_level = -1;
		a_tier = 0;
		auto lv = rest.substr(colon + 1);
		if (const auto tc = lv.find(':'); tc != std::string_view::npos) {
			const auto t = lv.substr(tc + 1);
			if (t.size() != 1 || t[0] < '0' || t[0] > '9') {
				return false;
			}
			a_tier = t[0] - '0';
			lv = lv.substr(0, tc);
		}
		for (int i = 0; i < 4; ++i) {
			if (lv == std::to_string(kLevels[i])) a_level = i;
		}
		return a_layer >= 0 && a_level >= 0;
	}
}
