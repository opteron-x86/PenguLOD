#pragma once
#include <filesystem>
#include <map>
#include <optional>
#include <string>
#include <string_view>

namespace PenguLOD::Ini
{
	/// A parsed INI file. Section and key names are lower-cased; values are trimmed and lose a trailing inline comment
	/// ("0 ; off" reads as "0"). Lines starting with ';' or '#' are comments. A UTF-8 byte order mark is skipped.
	class Document
	{
	public:
		static Document Parse(std::string_view a_text);
		/// An empty document when the file is missing or unreadable.
		static Document Read(const std::filesystem::path& a_path);

		/// The value of a key in one section (case-insensitive), or null.
		const std::string* Get(std::string_view a_section, std::string_view a_key) const;
		/// The value of a key in any section, the last assignment in the file winning. Older settings files used other section
		/// names ([DynamicLOD], [QualityLayers]) for the same keys.
		const std::string* GetAny(std::string_view a_key) const;
		bool Empty() const { return _any.empty(); }

	private:
		std::map<std::string, std::map<std::string, std::string>> _sections;
		std::map<std::string, std::string>                        _any;
	};

	/// 1/0, true/false, yes/no, on/off (any case). Anything else is not a boolean.
	std::optional<bool> Bool(std::string_view a_value);
	/// A decimal number, independent of the process locale. The whole value must be the number.
	std::optional<float> Float(std::string_view a_value);
	/// A whole decimal number. The whole value must be the number.
	std::optional<long long> Integer(std::string_view a_value);

	std::string Lower(std::string_view a_value);
	std::string Trim(std::string_view a_value);
}
