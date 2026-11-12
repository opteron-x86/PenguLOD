#pragma once

#include "AdaptivePolicy.h"

namespace PenguLOD
{
	/// Adaptive quality: when the frame rate stays below a target for a while, steps the quality layers (grass, 3D trees, tree cards) down one
	/// preset (Max, High, Medium, Low); when there is plenty of headroom for long enough, steps back up. Off by default.
	///
	/// The frame rate is read from the swap chain's present counter once a second, so no engine hook is involved. Time spent in menus or on a
	/// loading screen is ignored. A step up is held back longer each time it was followed by a step down again, so it settles instead of flapping.
	/// With frame generation on, the counter counts every presented frame, generated ones included: set the target accordingly.
	class Adaptive
	{
	public:
		static Adaptive& Get();

		/// Starts the once-a-second timer that queues Tick() on the main thread.
		void Start();
		/// Back to the first preset (everything shown) and forget the history.
		void Reset();
		/// The quality layers were reloaded from PenguLOD.ini: re-apply the preset adaptive quality had set (main thread).
		void Reapply();

		float Fps() const { return _fps; }
		/// A detected frame cap (0 = none), and the target in use, which stays below a cap.
		float Cap() const { return _cap; }
		float EffectiveTarget() const { return _target; }
		int   Level() const { return _level; }
		bool  Available() const { return _available; }
		static const char* LevelName(int a_level);

	private:
		void Tick();
		void Apply(int a_level);

		std::atomic<bool>  _running{ false };
		std::atomic<bool>  _queued{ false };
		std::atomic<float> _fps{ 0.0f };
		std::atomic<float> _cap{ 0.0f };
		std::atomic<float> _target{ 0.0f };
		std::atomic<int>   _level{ 0 };
		std::atomic<bool>  _available{ true };

		// main thread only
		std::uint32_t                         _lastCount{ 0 };
		std::chrono::steady_clock::time_point _lastTime{};
		bool                                  _haveLast{ false };
		AdaptivePolicy                        _policy;
		bool                                  _warned{ false };
	};
}
