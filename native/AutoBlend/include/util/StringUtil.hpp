#pragma once

#include <string>

/**
 * @brief UTF-8/UTF-16 conversion helpers. Trimmed down from AutoSeasons' own StringUtil (which
 * also handled Windows-1252/ASCII codepages and vector/JSON-array helpers for its plugin-string
 * reading code) to just what AutoBlend actually needs: settings.json round-trips UTF-8 text,
 * wxString/std::filesystem::path want UTF-16 - nothing here ever touches a legacy codepage.
 */
namespace StringUtil {

/**
 * @brief Converts a UTF-8 encoded narrow string to a UTF-16 wide string.
 */
auto utf8toUTF16(const std::string& str) -> std::wstring;

/**
 * @brief Converts a UTF-16 wide string to a UTF-8 encoded narrow string.
 */
auto utf16toUTF8(const std::wstring& str) -> std::string;

} // namespace StringUtil
