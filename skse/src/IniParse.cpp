#include "IniParse.h"

#include <cctype>
#include <charconv>
#include <cmath>
#include <fstream>
#include <iterator>

namespace PenguLOD::Ini
{
	std::string Lower(std::string_view a_value)
	{
		std::string out(a_value);
		for (auto& c : out) c = static_cast<char>(std::tolower(static_cast<unsigned char>(c)));
		return out;
	}

	std::string Trim(std::string_view a_value)
	{
		const auto a = a_value.find_first_not_of(" \t\r\n");
		if (a == std::string_view::npos) return {};
		const auto b = a_value.find_last_not_of(" \t\r\n");
		return std::string(a_value.substr(a, b - a + 1));
	}

	namespace
	{
		/// Cuts an inline comment: ';' or '#' at the start or after whitespace.
		std::string_view StripComment(std::string_view a_value)
		{
			for (std::size_t i = 0; i < a_value.size(); ++i) {
				if ((a_value[i] == ';' || a_value[i] == '#') && (i == 0 || a_value[i - 1] == ' ' || a_value[i - 1] == '\t')) {
					return a_value.substr(0, i);
				}
			}
			return a_value;
		}
	}

	Document Document::Parse(std::string_view a_text)
	{
		Document doc;
		if (a_text.starts_with("\xEF\xBB\xBF")) a_text.remove_prefix(3);
		std::string section;
		while (!a_text.empty()) {
			const auto nl = a_text.find('\n');
			auto       line = a_text.substr(0, nl);
			a_text = nl == std::string_view::npos ? std::string_view{} : a_text.substr(nl + 1);
			const auto t = Trim(line);
			if (t.empty() || t[0] == ';' || t[0] == '#') continue;
			if (t[0] == '[') {
				const auto close = t.find(']');
				section = Lower(Trim(t.substr(1, close == std::string::npos ? std::string::npos : close - 1)));
				continue;
			}
			const auto eq = t.find('=');
			if (eq == std::string::npos) continue;
			auto key = Lower(Trim(t.substr(0, eq)));
			if (key.empty()) continue;
			auto value = Trim(StripComment(std::string_view(t).substr(eq + 1)));
			doc._sections[section][key] = value;
			doc._any[std::move(key)] = std::move(value);
		}
		return doc;
	}

	Document Document::Read(const std::filesystem::path& a_path)
	{
		std::ifstream in(a_path, std::ios::binary);
		if (!in) return {};
		const std::string text((std::istreambuf_iterator<char>(in)), std::istreambuf_iterator<char>());
		return Parse(text);
	}

	const std::string* Document::Get(std::string_view a_section, std::string_view a_key) const
	{
		const auto s = _sections.find(Lower(a_section));
		if (s == _sections.end()) return nullptr;
		const auto k = s->second.find(Lower(a_key));
		return k == s->second.end() ? nullptr : &k->second;
	}

	const std::string* Document::GetAny(std::string_view a_key) const
	{
		const auto k = _any.find(Lower(a_key));
		return k == _any.end() ? nullptr : &k->second;
	}

	std::optional<bool> Bool(std::string_view a_value)
	{
		const auto v = Lower(Trim(a_value));
		if (v == "1" || v == "true" || v == "yes" || v == "on") return true;
		if (v == "0" || v == "false" || v == "no" || v == "off") return false;
		return std::nullopt;
	}

	namespace
	{
		template <class T>
		std::optional<T> Number(std::string_view a_value)
		{
			auto v = Trim(a_value);
			std::string_view text = v;
			if (text.starts_with('+')) text.remove_prefix(1);
			if (text.empty()) return std::nullopt;
			T result{};
			const auto [end, ec] = std::from_chars(text.data(), text.data() + text.size(), result);
			if (ec != std::errc{} || end != text.data() + text.size()) return std::nullopt;
			return result;
		}
	}

	std::optional<float> Float(std::string_view a_value)
	{
		auto v = Number<float>(a_value);
		if (v && !std::isfinite(*v)) return std::nullopt;  // NaN and infinity are not settings
		return v;
	}

	std::optional<long long> Integer(std::string_view a_value) { return Number<long long>(a_value); }
}
