#pragma once

#include <wx/string.h>

#include <filesystem>
#include <string>
#include <vector>

/**
 * @class ABLocale
 * @brief GUI localization system backed by JSON translation files
 *
 * Translation files live in the "AutoBlend_translations" folder next to the executable, one file per language named
 * by its IETF/ISO language code (e.g. "en.json", "fr.json"). The schema is i18next-style nested JSON: nested objects
 * whose leaf string values are addressed by dot-separated keys, e.g. {"launcher": {"title": "..."}} is looked up as
 * "launcher.title". A reserved top-level "_language" key holds the language's native display name shown in the
 * language selector.
 *
 * Every lookup carries a hardcoded English default which is returned when the key is missing from the active
 * translation (or when no translation file is loaded at all), so English is the built-in fallback language.
 *
 * Mirrors AutoSeasons' own ASLocale (same file layout, same lookup semantics) - this is a straight port, not a
 * from-scratch design, so a contributor already familiar with one recognizes the other immediately.
 */
class ABLocale {
public:
    struct Language {
        std::string code; ///< Language code, also the translation filename stem (e.g. "en")
        wxString displayName; ///< Native display name (e.g. "English", "Français")
    };

    /**
     * @brief Initializes the locale system and loads the translation for the given language code.
     *
     * @param translationsDir Folder containing the translation JSON files.
     * @param langCode Language code to load (file <langCode>.json); missing files fall back to hardcoded English.
     */
    static void init(const std::filesystem::path& translationsDir, const std::string& langCode);

    /**
     * @brief Looks up a translated string by key.
     *
     * @param key Dot-separated translation key (e.g. "launcher.title").
     * @param defaultValue Hardcoded English fallback used when the key is missing from the active translation.
     * @return Translated string, or the fallback.
     */
    [[nodiscard]] static auto tr(const std::string& key, const char* defaultValue) -> wxString;

    /// @brief Gets the language code that is currently active.
    [[nodiscard]] static auto getCurrentLanguage() -> std::string;

    /// @brief Lists the languages available in the translations folder (sorted by display name).
    [[nodiscard]] static auto getAvailableLanguages() -> std::vector<Language>;
};

/// @brief Shorthand for ABLocale::tr.
[[nodiscard]] inline auto ABTr(const std::string& key, const char* defaultValue) -> wxString
{
    return ABLocale::tr(key, defaultValue);
}
