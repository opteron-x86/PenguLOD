// Linux tests for the runtime's CommonLib-free parts: INI parsing and settings, own-write tracking, the dynamic LOD show plan,
// the adaptive quality policy, layer node names and the binary data readers (including the generator's own output).
#include "AdaptivePolicy.h"
#include "IniParse.h"
#include "LayerNames.h"
#include "Settings.h"
#include "ShowPlan.h"

#include <clocale>
#include <iostream>
#include <thread>

namespace fs = std::filesystem;
using namespace PenguLOD;

#define CHECK(x)                                                                    \
	do {                                                                            \
		if (!(x)) {                                                                 \
			std::cerr << __FILE__ << ":" << __LINE__ << ": check failed: " #x "\n"; \
			std::exit(1);                                                           \
		}                                                                           \
	} while (false)

static void WriteText(const fs::path& a_path, const std::string& a_text)
{
	fs::create_directories(a_path.parent_path());
	std::ofstream(a_path, std::ios::binary) << a_text;
}

static std::vector<char> Bytes(const fs::path& a_path)
{
	std::ifstream in(a_path, std::ios::binary);
	return { std::istreambuf_iterator<char>(in), std::istreambuf_iterator<char>() };
}

static void IniParsing()
{
	const auto doc = Ini::Document::Parse("\xEF\xBB\xBF; header\n[Dynamic]\r\nbEnabled = 0 ; off\nfMaxDistance=50000.5\n# note\n[Layers]\nbShow_grass_4=no\nKey=a;b\n[Other]\nbEnabled=1\n");
	CHECK(doc.Get("dynamic", "BENABLED") && *doc.Get("dynamic", "BENABLED") == "0");
	CHECK(*doc.GetAny("bEnabled") == "1");  // the last assignment wins across sections
	CHECK(*doc.Get("Layers", "key") == "a;b"); // ';' inside a value is kept, only a comment after whitespace is cut
	CHECK(!doc.Get("Missing", "bEnabled"));
	CHECK(Ini::Bool("yes") == true && Ini::Bool(" OFF ") == false && Ini::Bool("On") == true && !Ini::Bool("2"));
	CHECK(Ini::Integer("42") == 42 && !Ini::Integer("1.5") && !Ini::Integer("12abc") && Ini::Integer("+7") == 7);
	CHECK(!Ini::Float("nan") && !Ini::Float("inf") && !Ini::Float("") && !Ini::Float("1,5"));
	// Numbers never follow the process locale (std::stof would read "1.5" as 1 under a decimal-comma locale).
	if (std::setlocale(LC_NUMERIC, "de_DE.UTF-8")) {
		CHECK(Ini::Float("1.5") == 1.5f);
		std::setlocale(LC_NUMERIC, "C");
	}
	CHECK(Ini::Document::Read("missing.ini").Empty());
}

static void SettingsLoading()
{
	WriteText(RuntimePaths::Settings, "[Dynamic]\nbEnabled=no ; disabled\niMaxShown=1.5\nfMaxDistance=50000.5\niMaxShowPerUpdate=0\nbVerboseLog=maybe\n");
	auto& s = Settings::Get();
	s = Settings{};
	s.Load();
	CHECK(!s.enabled);                    // "no" was read as true by the old parser
	CHECK(s.maxShown == 8000);            // not a whole number: the default stays
	CHECK(s.maxDistance == 50000.5f);
	CHECK(s.maxShowPerUpdate == 1);       // clamped to at least one object per update
	CHECK(!s.verboseLog);
	fs::remove(RuntimePaths::Settings);
	WriteText(RuntimePaths::LegacySettings, "[DynamicLOD]\niMaxShown=123\n");  // the old file and section name
	s = Settings{};
	s.Load();
	CHECK(s.maxShown == 123);
	fs::remove(RuntimePaths::LegacySettings);
}

static void OwnWrites()
{
	CHECK(IniFile::Write(RuntimePaths::Settings, "Layers", { { "iGrassTiers", "2" } }));
	std::error_code ec;
	const auto own = fs::last_write_time(RuntimePaths::Settings, ec);
	CHECK(!ec && IniFile::IsOwnWrite(RuntimePaths::Settings, own));
	fs::last_write_time(RuntimePaths::Settings, own + std::chrono::seconds(5));  // an editor saving the file later
	CHECK(!IniFile::IsOwnWrite(RuntimePaths::Settings, fs::last_write_time(RuntimePaths::Settings)));
	fs::remove(RuntimePaths::Settings);
}

static void ShowPlanning()
{
	using ShowPlan::Candidate;
	// Room under the cap: nearest first, limited by the per-update budget.
	auto r = ShowPlan::Plan({}, { { 9, 0 }, { 1, 1 }, { 4, 2 } }, 10, 2);
	CHECK((r.show == std::vector<std::size_t>{ 1, 2 }) && r.deferred == 1 && r.evict.empty() && !r.capped);
	// At the cap a nearer object replaces the farthest drawn one; farther ones stay out.
	r = ShowPlan::Plan({ { 100, 10 }, { 4, 11 } }, { { 1, 0 }, { 200, 1 } }, 2, 50);
	CHECK((r.show == std::vector<std::size_t>{ 0 }) && (r.evict == std::vector<std::size_t>{ 10 }) && r.capped);
	// A lowered cap drops the farthest drawn objects.
	r = ShowPlan::Plan({ { 1, 10 }, { 3, 11 }, { 2, 12 } }, {}, 1, 50);
	CHECK((r.evict == std::vector<std::size_t>{ 11, 12 }) && r.show.empty());
	// Cap 0 draws nothing.
	r = ShowPlan::Plan({}, { { 1, 0 } }, 0, 50);
	CHECK(r.show.empty() && r.capped);
}

static void AdaptiveQuality()
{
	// A 60 fps cap with the target at 60: the measured rate never exceeds the cap. Before, every second counted as "low", so the
	// layers sank to the floor and never came back.
	AdaptivePolicy p;
	int level = 0;
	for (int t = 0; t < 600; ++t) level = p.Step(59.9f, 60.0f, 3, level);
	CHECK(p.Capped());
	CHECK(p.EffectiveTarget(60.0f) < 59.9f);
	CHECK(level == 0);
	// Below the (lowered) target it steps down, and with the cap reached again it steps back up.
	for (int t = 0; t < 30; ++t) level = p.Step(40.0f, 60.0f, 3, level);
	CHECK(level >= 1);
	const int lowered = level;
	for (int t = 0; t < 2000; ++t) level = p.Step(59.9f, 60.0f, 3, level);
	CHECK(level < lowered);

	// Uncapped: headroom means well above the target.
	AdaptivePolicy u;
	level = 2;
	for (int t = 0; t < 200; ++t) level = u.Step(80.0f + static_cast<float>(t % 7) * 9.0f, 40.0f, 3, level);  // varies: no cap
	CHECK(!u.Capped() && level < 2);
	// The floor is honoured at once.
	CHECK(u.Step(10.0f, 40.0f, 1, 3) == 1);
	CHECK(AdaptivePolicy::IsCommonCap(144.5f) && !AdaptivePolicy::IsCommonCap(53.0f));
}

static void LayerNodeNames()
{
	int layer = 0, level = 0, tier = 0;
	CHECK(LayerNames::Parse("PL:grass:16:2", layer, level, tier) && layer == 0 && level == 2 && tier == 2);
	CHECK(LayerNames::Parse("AL:treecard:32", layer, level, tier) && layer == 2 && level == 3 && tier == 0);
	CHECK(!LayerNames::Parse("PL:grass:12", layer, level, tier));
	CHECK(!LayerNames::Parse("PL:grass:16:10", layer, level, tier));
	CHECK(!LayerNames::Parse("PL:bushes:4", layer, level, tier));
	CHECK(!LayerNames::Parse(nullptr, layer, level, tier));
}

template <class File>
static bool Reads(const std::vector<char>& a_bytes)
{
	{
		std::ofstream out("data.bin", std::ios::binary | std::ios::trunc);
		out.write(a_bytes.data(), static_cast<std::streamsize>(a_bytes.size()));
	}
	File file;
	return file.Read("data.bin");
}

template <class File>
static void Malformed(const std::vector<char>& a_good, std::size_t a_entryCountAt)
{
	CHECK(Reads<File>(a_good));
	for (std::size_t n = 0; n < a_good.size(); ++n) {
		CHECK(!Reads<File>(std::vector<char>(a_good.begin(), a_good.begin() + static_cast<std::ptrdiff_t>(n))));  // every truncation
	}
	auto version = a_good;
	version[4] = 99;
	CHECK(!Reads<File>(version));
	auto magic = a_good;
	magic[0] = 'X';
	CHECK(!Reads<File>(magic));
	for (const std::size_t at : { std::size_t{ 8 }, a_entryCountAt }) {  // the string count, then the entry count
		auto huge = a_good;
		const std::uint32_t count = 0x7FFFFFFF;
		std::memcpy(huge.data() + at, &count, 4);
		CHECK(!Reads<File>(huge));  // refused before allocating, not std::bad_alloc
	}
}

static std::size_t EntryCountOffset(const std::vector<char>& a_file)
{
	std::uint32_t strings = 0;
	std::memcpy(&strings, a_file.data() + 8, 4);
	std::size_t at = 12;
	for (std::uint32_t i = 0; i < strings; ++i) {
		std::uint16_t len = 0;
		std::memcpy(&len, a_file.data() + at, 2);
		at += 2 + len;
	}
	return at;
}

static void GeneratorOutput(const fs::path& a_fixtures)
{
	// Written by the C# writers (tests/PenguLOD.Core.Tests/RuntimeDataGoldenTests.cs asserts they still produce these bytes).
	DataFile dyn;
	CHECK(dyn.Read(a_fixtures / "PenguLOD.dyn"));
	CHECK(dyn.entries.size() == 3);
	const auto& water = dyn.entries[0];
	CHECK(dyn.strings[water.refPlugin] == "W\xC3\xA4sserf\xC3\xA4lle.esp" && water.refLocalId == 0x801);
	CHECK(water.flags == (DynamicEntry::kGridObject | DynamicEntry::kNearGrid) && water.parentPlugin == DynamicEntry::kNone);
	CHECK(dyn.strings[water.mesh] == "effects\\fxwaterfallbodytall.nif" && water.pos[0] == -4096.0f && water.pos[2] == -64.0f);
	const auto& glow = dyn.entries[1];
	CHECK(glow.flags == (DynamicEntry::kGridObject | DynamicEntry::kNeverFade | DynamicEntry::kGlow) && glow.scale == 2.0f);
	const auto& farm = dyn.entries[2];
	CHECK(dyn.strings[farm.refPlugin] == "Skyrim.esm" && farm.refLocalId == 0x12345 && dyn.strings[farm.worldPlugin] == "Skyrim.esm" && farm.worldLocalId == 0x3C);
	CHECK(dyn.strings[farm.parentPlugin] == "Update.esm" && farm.parentLocalId == 0x55);
	CHECK(farm.flags == (DynamicEntry::kOpposite | DynamicEntry::kInitiallyDisabled));
	CHECK(farm.pos[0] == 100.5f && farm.pos[1] == -200.25f && farm.rot[2] == 0.3f && farm.scale == 1.25f);
	CHECK(dyn.strings[farm.mesh] == "lod\\farmhouse\\farmhouse01_lod_0.nif");

	CopyFile copies;
	CHECK(copies.Read(a_fixtures / "PenguLOD.copies"));
	CHECK(copies.entries.size() == 2);
	CHECK(copies.strings[copies.entries[0].model] == "landscape\\rocks\\rockl01.nif" && copies.entries[0].worldLocalId == 0x1A26F);
	CHECK(copies.entries[0].flags == CopyEntry::kChildCell && copies.entries[0].scale == 1.5f && copies.entries[0].rot[1] == 0.2f);
	CHECK(copies.entries[1].flags == CopyEntry::kAltTextures && copies.entries[1].pos[2] == 6.0f);

	NativeFile natives;
	CHECK(natives.Read(a_fixtures / "PenguLOD.native"));
	CHECK(natives.entries.size() == 2);
	const auto& fall = natives.entries[0];
	CHECK(natives.strings[fall.sourcePlugin] == "Skyrim.esm" && fall.sourceLocalId == 0x1F00D && natives.strings[fall.nativePlugin] == "PenguLOD.esp");
	CHECK(fall.nativeLocalId == 0x801 && natives.strings[fall.parentPlugin] == "Skyrim.esm" && fall.parentLocalId == 0x1F00E);
	CHECK(fall.flags == NativeEntry::kOpposite && fall.x == 10.0f && fall.z == 30.0f);
	const auto& fire = natives.entries[1];
	CHECK(fire.flags == (NativeEntry::kInitiallyDisabled | NativeEntry::kNear) && fire.parentPlugin == NativeEntry::kNone);
	CHECK(natives.strings[fire.nativePlugin] == "PenguLOD Effects.esp" && fire.y == -6.0f);

	// The same files, damaged: no crash, no huge allocation, just a refusal.
	const auto dynBytes = Bytes(a_fixtures / "PenguLOD.dyn");
	const auto copyBytes = Bytes(a_fixtures / "PenguLOD.copies");
	const auto nativeBytes = Bytes(a_fixtures / "PenguLOD.native");
	Malformed<DataFile>(dynBytes, EntryCountOffset(dynBytes));
	Malformed<CopyFile>(copyBytes, EntryCountOffset(copyBytes));
	Malformed<NativeFile>(nativeBytes, EntryCountOffset(nativeBytes));
	// A string index past the table.
	auto badIndex = dynBytes;
	const std::uint32_t index = 0xFFFF;
	std::memcpy(badIndex.data() + EntryCountOffset(dynBytes) + 4 + 16, &index, 4);  // the first entry's mesh index
	CHECK(!Reads<DataFile>(badIndex));
}

int main(int argc, char** argv)
{
	CHECK(argc == 3);
	const fs::path fixtures = fs::absolute(argv[2]);
	fs::current_path(argv[1]);
	IniParsing();
	SettingsLoading();
	OwnWrites();
	ShowPlanning();
	AdaptiveQuality();
	LayerNodeNames();
	GeneratorOutput(fixtures);
	std::cout << "Runtime logic, settings and data file checks passed\n";
}
