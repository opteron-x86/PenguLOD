#pragma once
#include <algorithm>
#include <array>
#include <cmath>

namespace PenguLOD
{
	/// The decision part of adaptive quality, fed one smoothed frame rate a second. Level 0 is Max; higher levels show less.
	///
	/// A frame cap (V-sync, a limiter, frame generation) keeps the measured rate flat at the cap however much headroom the GPU has,
	/// so "well above the target" never happens there. When the rate has sat at a common cap value for a while, being at the cap
	/// counts as headroom, and a target at or above the cap is lowered just below it (otherwise every step goes down and none back up).
	class AdaptivePolicy
	{
	public:
		static constexpr float kHeadroom = 1.35f;      // uncapped: step up only when this far above the target
		static constexpr float kAtCap = 0.97f;         // within 3% of the cap counts as running at the cap
		static constexpr float kBelowCap = 0.92f;      // a target at or above a cap is lowered to this share of it
		static constexpr int   kCapSeconds = 20;       // seconds at the cap before it is taken for one

		void Reset()
		{
			Pause();
			_sinceChange = 1000;
			_upHold = 60;
			_lastWasUp = false;
			_ceiling = 0.0f;
			_atCeiling = 0;
		}

		/// Menus, pauses, loading screens: the counts restart.
		void Pause()
		{
			_lowSeconds = 0;
			_highSeconds = 0;
		}

		/// One second of play. Returns the level to use from now on (a_level when nothing changes).
		int Step(float a_fps, float a_target, int a_floor, int a_level)
		{
			const int lowest = std::clamp(a_floor, 0, 3);
			Observe(a_fps);
			++_sinceChange;
			if (a_level > lowest) {
				// The floor was raised (menu or ini) above where it had gone: come back to it at once.
				return Changed(lowest, false);
			}
			const float target = EffectiveTarget(a_target);
			const bool  headroom = a_fps > target * kHeadroom || (Capped() && a_fps >= _ceiling * kAtCap);
			if (a_fps < target) {
				++_lowSeconds;
				_highSeconds = 0;
			} else if (headroom) {
				++_highSeconds;
				_lowSeconds = 0;
			} else {
				Pause();
			}

			if (_lowSeconds >= 8 && a_level < lowest && _sinceChange >= 15) {
				// Having to step down again soon after a step up means the headroom was only the lower setting's: wait longer next time.
				if (_lastWasUp && _sinceChange < 180) {
					_upHold = std::min(_upHold * 2, 900);
				}
				return Changed(a_level + 1, false);
			}
			if (_highSeconds >= _upHold && a_level > 0 && _sinceChange >= 30) {
				return Changed(a_level - 1, true);
			}
			return a_level;
		}

		/// The target actually used: below a detected cap.
		float EffectiveTarget(float a_target) const { return Capped() ? std::min(a_target, _ceiling * kBelowCap) : a_target; }
		bool  Capped() const { return _atCeiling >= kCapSeconds && IsCommonCap(_ceiling); }
		float Ceiling() const { return _ceiling; }
		int   UpHold() const { return _upHold; }

		static bool IsCommonCap(float a_fps)
		{
			constexpr std::array<float, 13> caps{ 30, 45, 48, 50, 60, 72, 75, 90, 100, 120, 144, 165, 240 };
			return std::ranges::any_of(caps, [&](float c) { return std::abs(a_fps - c) <= c * 0.03f; });
		}

	private:
		void Observe(float a_fps)
		{
			if (a_fps > _ceiling * 1.03f) {
				// A higher rate than any so far: not capped where we thought.
				_ceiling = a_fps;
				_atCeiling = 0;
			} else if (a_fps >= _ceiling * kAtCap) {
				_ceiling = std::max(_ceiling, a_fps);
				++_atCeiling;
			}
		}

		int Changed(int a_level, bool a_up)
		{
			Pause();
			_sinceChange = 0;
			_lastWasUp = a_up;
			return a_level;
		}

		int   _lowSeconds{ 0 };
		int   _highSeconds{ 0 };
		int   _sinceChange{ 1000 };
		int   _upHold{ 60 };        // seconds of headroom needed before stepping back up
		bool  _lastWasUp{ false };
		float _ceiling{ 0.0f };     // the highest frame rate seen (a cap shows up as a flat ceiling)
		int   _atCeiling{ 0 };      // seconds spent at it
	};
}
