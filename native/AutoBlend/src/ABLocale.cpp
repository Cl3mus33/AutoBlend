#include "ABLocale.hpp"

#include "util/FileUtil.hpp"

#include <nlohmann/json.hpp>

#include <algorithm>
#include <exception>
#include <iterator>
#include <unordered_map>

using namespace std;

namespace {

filesystem::path s_translationsDir;
string s_currentLanguage = "en";
unordered_map<string, wxString> s_strings;

auto parseTranslationFile(const filesystem::path& file, nlohmann::json& j) -> bool
{
    try {
        const auto fileBytes = FileUtil::getFileBytes(file);
        string fileStr;
        fileStr.reserve(fileBytes.size());
        ranges::transform(fileBytes, back_inserter(fileStr), [](std::byte b) { return static_cast<char>(b); });
        j = nlohmann::json::parse(fileStr);
    } catch (const exception&) {
        return false;
    }

    return j.is_object();
}

void flattenJSON(const nlohmann::json& j, const string& prefix, unordered_map<string, wxString>& out)
{
    for (const auto& [key, value] : j.items()) {
        if (prefix.empty() && key.starts_with("_")) {
            continue; // reserved metadata keys (e.g. "_language")
        }

        const string fullKey = prefix.empty() ? key : prefix + "." + key;
        if (value.is_object()) {
            flattenJSON(value, fullKey, out);
        } else if (value.is_string()) {
            out[fullKey] = wxString::FromUTF8(value.get<string>());
        }
    }
}

auto getLanguageDisplayName(const filesystem::path& file, const string& code) -> wxString
{
    nlohmann::json j;
    if (parseTranslationFile(file, j) && j.contains("_language") && j["_language"].is_string()) {
        return wxString::FromUTF8(j["_language"].get<string>());
    }

    return wxString::FromUTF8(code);
}

} // namespace

void ABLocale::init(const filesystem::path& translationsDir, const string& langCode)
{
    s_translationsDir = translationsDir;
    s_currentLanguage = langCode.empty() ? "en" : langCode;
    s_strings.clear();

    const auto translationFile = s_translationsDir / (s_currentLanguage + ".json");
    if (!filesystem::exists(translationFile)) {
        return;
    }

    nlohmann::json j;
    if (parseTranslationFile(translationFile, j)) {
        flattenJSON(j, "", s_strings);
    }
}

auto ABLocale::tr(const string& key, const char* defaultValue) -> wxString
{
    const auto it = s_strings.find(key);
    if (it != s_strings.end() && !it->second.empty()) {
        return it->second;
    }

    return wxString::FromUTF8(defaultValue);
}

auto ABLocale::getCurrentLanguage() -> string { return s_currentLanguage; }

auto ABLocale::getAvailableLanguages() -> vector<Language>
{
    vector<Language> languages;

    if (s_translationsDir.empty() || !filesystem::exists(s_translationsDir)) {
        return languages;
    }

    for (const auto& entry : filesystem::directory_iterator(s_translationsDir)) {
        if (!entry.is_regular_file() || entry.path().extension() != ".json") {
            continue;
        }

        const auto code = entry.path().stem().string();
        languages.push_back({.code = code, .displayName = getLanguageDisplayName(entry.path(), code)});
    }

    ranges::sort(languages,
        [](const Language& a, const Language& b) -> bool { return a.displayName.CmpNoCase(b.displayName) < 0; });

    return languages;
}
