#pragma once

#include <string>

#include <wx/string.h>

/**
 * @brief Translation lookup stub. English-only for now (returns defaultValue unconditionally) -
 * every user-facing GUI string is still routed through here (same call shape as AutoSeasons' own
 * ASTr) so that adding real translations later (JSON files + a language switcher, mirroring
 * AutoSeasons' ASLocale) only means changing this one function's body, not touching every call
 * site in LauncherWindow/ProgressWindow.
 *
 * @param key Dot-separated translation key (e.g. "launcher.title") - unused today, kept so call
 * sites already carry the key a future translation table would look up.
 * @param defaultValue Hardcoded English string, returned as-is.
 */
[[nodiscard]] inline auto ABTr(const std::string& /*key*/, const char* defaultValue) -> wxString
{
    return wxString::FromUTF8(defaultValue);
}
