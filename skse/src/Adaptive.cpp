#include "Adaptive.h"

#include "LodLayers.h"
#include "Settings.h"

#if PENGULOD_RUNTIME_LINE != 14
#	include <dxgi.h>
#endif

namespace PenguLOD
{
	Adaptive& Adaptive::Get()
	{
		static Adaptive instance;
		return instance;
	}

	const char* Adaptive::LevelName(int a_level)
	{
		switch (a_level) {
		case 0: return "Max (everything shown)";
		case 1: return "High (3D trees only at LOD4)";
		case 2: return "Medium (grass at LOD4 and LOD8)";
		default: return "Low (grass at LOD4 only)";
		}
	}

	void Adaptive::Start()
	{
		if (_running.exchange(true)) {
			return;
		}
		std::thread([this] {
			for (;;) {
				std::this_thread::sleep_for(1s);
				if (_queued.exchange(true)) {
					continue;
				}
				SKSE::GetTaskInterface()->AddTask([this] {
					Tick();
					_queued = false;
				});
			}
		}).detach();
	}

	void Adaptive::Reset()
	{
		_policy.Reset();
		if (_level != 0) {
			Apply(0);
		}
	}

	void Adaptive::Reapply()
	{
		if (Settings::Get().adaptiveQuality && _level != 0) {
			Layers::Get().Preset(_level);
		}
	}

	void Adaptive::Apply(int a_level)
	{
		_level = a_level;
		Layers::Get().Preset(a_level);
		logger::info("Adaptive quality: layers set to {} (frame rate {:.0f})", LevelName(a_level), _fps.load());
	}

	void Adaptive::Tick()
	{
#if PENGULOD_RUNTIME_LINE == 14
		_available = false;  // Skyrim VR presents through the compositor: there is no swap chain to count
		return;
#else
		const auto& s = Settings::Get();

		// The frame rate: presents since the last tick over the time since the last tick.
#	if PENGULOD_RUNTIME_LINE == 17
		// CommonLib 7.2.0 (Skyrim 1.7.x) keeps the renderer's windows behind an accessor and wraps the swap chain type.
		auto* window = RE::BSGraphics::Renderer::GetCurrentRenderWindow();
		auto* swap = window ? reinterpret_cast<IDXGISwapChain*>(window->swapChain) : nullptr;
#	else
		auto* renderer = RE::BSGraphics::Renderer::GetSingleton();
		auto* swap = renderer ? renderer->data.renderWindows[0].swapChain : nullptr;
#	endif
		UINT  count = 0;
		if (!swap || FAILED(swap->GetLastPresentCount(&count))) {
			if (!_warned) {
				logger::warn("Adaptive quality: the swap chain gives no present count; the frame rate can't be read");
				_warned = true;
			}
			_available = false;
			return;
		}
		_available = true;
		const auto now = std::chrono::steady_clock::now();
		float      fps = 0.0f;
		bool       measured = false;
		if (_haveLast) {
			const float dt = std::chrono::duration<float>(now - _lastTime).count();
			if (dt > 0.2f) {
				fps = static_cast<float>(count - _lastCount) / dt;
				measured = true;
			}
		}
		_lastCount = count;
		_lastTime = now;
		_haveLast = true;
		if (!measured) {
			return;
		}

		// Menus, pauses and loading screens say nothing about how the game runs.
		auto* ui = RE::UI::GetSingleton();
		if (ui && (ui->GameIsPaused() || ui->IsMenuOpen(RE::LoadingMenu::MENU_NAME))) {
			_policy.Pause();
			return;
		}
		_fps = _fps.load() <= 0.0f ? fps : _fps.load() * 0.7f + fps * 0.3f;

		if (!s.adaptiveQuality) {
			// Turned off after it had lowered the layers: put them back.
			if (_level != 0) {
				Reset();
			}
			_policy.Pause();
			return;
		}

		const int next = _policy.Step(_fps, static_cast<float>(s.adaptiveTargetFps), s.adaptiveFloor, _level);
		_cap = _policy.Capped() ? _policy.Ceiling() : 0.0f;
		_target = _policy.EffectiveTarget(static_cast<float>(s.adaptiveTargetFps));
		if (next != _level) {
			Apply(next);
		}
#endif
	}
}
