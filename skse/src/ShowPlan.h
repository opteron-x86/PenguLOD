#pragma once
#include <algorithm>
#include <cstddef>
#include <vector>

namespace PenguLOD::ShowPlan
{
	/// An object by its squared horizontal distance from the player.
	struct Candidate
	{
		float       distanceSq;
		std::size_t index;
	};

	struct Result
	{
		std::vector<std::size_t> show;      // to draw now, nearest first
		std::vector<std::size_t> evict;     // drawn objects to drop: farther than something nearer that wants their place
		std::size_t              deferred{ 0 };  // wanted objects left for the next update by the per-update budget
		bool                     capped{ false }; // the cap keeps farther wanted objects out
	};

	/// Decides which wanted objects to draw this update. Never more than a_cap are drawn: at the cap a nearer wanted object
	/// replaces the farthest drawn one, so the objects next to the player are never the ones missing. At most a_budget objects
	/// are loaded per update (each is a model load and clone on the main thread); the rest wait for the next update.
	inline Result Plan(std::vector<Candidate> a_shown, std::vector<Candidate> a_wanted, std::size_t a_cap, std::size_t a_budget)
	{
		Result r;
		std::ranges::sort(a_wanted, {}, &Candidate::distanceSq);
		std::ranges::sort(a_shown, std::ranges::greater{}, &Candidate::distanceSq);  // farthest first
		std::size_t next = 0;  // next drawn object to evict
		std::size_t count = a_shown.size();
		// The cap was lowered (menu or ini): drop the farthest until it fits.
		while (count > a_cap && next < a_shown.size()) {
			r.evict.push_back(a_shown[next++].index);
			--count;
		}
		for (std::size_t w = 0; w < a_wanted.size(); ++w) {
			if (r.show.size() >= a_budget) {
				r.deferred = a_wanted.size() - w;
				break;
			}
			if (count < a_cap) {
				r.show.push_back(a_wanted[w].index);
				++count;
				continue;
			}
			if (next < a_shown.size() && a_shown[next].distanceSq > a_wanted[w].distanceSq) {
				r.evict.push_back(a_shown[next++].index);
				r.show.push_back(a_wanted[w].index);
				continue;
			}
			r.capped = true;  // every wanted object left is farther than everything drawn
			break;
		}
		return r;
	}
}
