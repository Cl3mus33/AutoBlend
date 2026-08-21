#include "ABConfig.hpp"

#include "util/FileUtil.hpp"
#include "util/Logger.hpp"
#include "util/StringUtil.hpp"

#include <nlohmann/json.hpp>

#include <algorithm>
#include <fstream>
#include <wchar.h>

// <windows.h> before <shlobj.h> so knownfolders.h's FOLDERID_* GUID definitions see properly
// set-up COM/GUID macros (DECLSPEC_SELECTANY etc.) - matches util/StringUtil.cpp's fix for the
// same class of "included a um/ header standalone" issue.
#include <windows.h>

#include <shlobj.h>

using namespace std;

namespace {
auto appDataDir() -> filesystem::path
{
    PWSTR rawPath = nullptr;
    filesystem::path result;
    if (SHGetKnownFolderPath(FOLDERID_RoamingAppData, 0, nullptr, &rawPath) == S_OK) {
        result = filesystem::path(rawPath);
    }
    if (rawPath != nullptr) {
        CoTaskMemFree(rawPath);
    }
    return result;
}
}

auto ABConfig::getConfigPath(const filesystem::path& exeDir) -> filesystem::path { return exeDir / "settings.json"; }

auto ABConfig::load(const filesystem::path& exeDir) -> ABParams
{
    const auto localPath = getConfigPath(exeDir);
    if (filesystem::exists(localPath)) {
        return loadFrom(localPath);
    }

    // First run for this particular copy of AutoBlend (a fresh per-modlist install, or upgrading
    // from a version that still used the single shared %APPDATA%\AutoBlend\settings.json) - fall
    // back to that old shared location once, so an existing user's Game Location/Output Location/
    // etc. carry forward instead of appearing blank, then let this copy's own settings.json (saved
    // on the next OK/Start) take over from here on. Never write anything back to the shared path -
    // every other copy of AutoBlend should get the exact same one-time fallback, not have it
    // consumed by whichever copy happens to run first.
    const auto sharedPath = appDataDir() / "AutoBlend" / "settings.json";
    if (filesystem::exists(sharedPath)) {
        return loadFrom(sharedPath);
    }

    return ABParams {};
}

void ABConfig::save(const filesystem::path& exeDir, const ABParams& params) { saveTo(getConfigPath(exeDir), params); }

auto ABConfig::loadFrom(const filesystem::path& configFilePath) -> ABParams
{
    ABParams params;

    nlohmann::json configJ;
    if (!FileUtil::getJSON(configFilePath, configJ)) {
        return params;
    }

    try {
        if (configJ.contains("UiLanguage")) {
            params.uiLanguage = configJ["UiLanguage"].get<string>();
        }
        if (configJ.contains("UiTheme")) {
            params.uiTheme = configJ["UiTheme"].get<string>();
        }
        if (configJ.contains("GameLocation")) {
            params.gameLocation = StringUtil::utf8toUTF16(configJ["GameLocation"].get<string>());
        }
        if (configJ.contains("GameType")) {
            params.gameType = static_cast<ABGameType>(configJ["GameType"].get<int>());
        }
        if (configJ.contains("OutputLocation")) {
            params.outputLocation = StringUtil::utf8toUTF16(configJ["OutputLocation"].get<string>());
        }
        if (configJ.contains("ModManager")) {
            params.modManager = static_cast<ABModManagerType>(configJ["ModManager"].get<int>());
        }
        if (configJ.contains("Mo2InstancePath")) {
            params.mo2InstancePath = StringUtil::utf8toUTF16(configJ["Mo2InstancePath"].get<string>());
        }
        if (configJ.contains("Mo2ProfileName")) {
            params.mo2ProfileName = StringUtil::utf8toUTF16(configJ["Mo2ProfileName"].get<string>());
        }
        if (configJ.contains("LandscapeFolderRules")) {
            params.landscapeFolderRules.clear();
            for (const auto& item : configJ["LandscapeFolderRules"]) {
                params.landscapeFolderRules.push_back({
                    StringUtil::utf8toUTF16(item.value("FolderName", string {})),
                    StringUtil::utf8toUTF16(item.value("TypeLabel", string {})),
                });
            }

            // A settings.json saved before the "blend" rule was introduced only has its own two
            // entries (statics/blending) - the block above replaces the list wholesale rather than
            // merging against ABParams' own default member initializer, so an existing user's file
            // would otherwise never pick up the new rule. Backfill it once here.
            const bool hasBlendRule = std::any_of(params.landscapeFolderRules.begin(), params.landscapeFolderRules.end(),
                [](const ABLandscapeFolderRule& r) { return _wcsicmp(r.folderName.c_str(), L"blend") == 0; });
            if (!hasBlendRule) {
                params.landscapeFolderRules.push_back({ L"blend", L"Blend" });
            }
        }
        if (configJ.contains("MeshBlacklist")) {
            params.meshBlacklist.clear();
            for (const auto& item : configJ["MeshBlacklist"]) {
                params.meshBlacklist.push_back(StringUtil::utf8toUTF16(item.get<string>()));
            }

            // Road-texture-replacer mods (Simplest Roads, Simply Dirt Roads, reported directly)
            // reuse an ordinary landscape texture's diffuse (e.g. Dirt02.dds) on their own road
            // meshes instead of a dedicated one. AutoBlend can't tell that reuse apart from a real
            // landscape mesh sharing the same texture, so it patched road meshes as if they were
            // landscape ones - producing malformed derived paths and wrong (e.g. snow) texture
            // assignments. Same backfill pattern as the "blend" rule above - a settings.json saved
            // before this fix only has the old two entries, so an existing user's file needs this
            // appended once too, not just fresh installs.
            const bool hasRoadsRule = std::any_of(params.meshBlacklist.begin(), params.meshBlacklist.end(),
                [](const std::wstring& pattern) { return _wcsicmp(pattern.c_str(), LR"(*\roads\*)") == 0; });
            if (!hasRoadsRule) {
                params.meshBlacklist.push_back(LR"(*\roads\*)");
            }
        }
        if (configJ.contains("EditorIdBlacklistKeywords")) {
            params.editorIdBlacklistKeywords.clear();
            for (const auto& item : configJ["EditorIdBlacklistKeywords"]) {
                params.editorIdBlacklistKeywords.push_back(StringUtil::utf8toUTF16(item.get<string>()));
            }
        }
        if (configJ.contains("TextureSetNamingTemplate")) {
            params.textureSetNamingTemplate = StringUtil::utf8toUTF16(configJ["TextureSetNamingTemplate"].get<string>());
        }
        if (configJ.contains("AutoGenerateMissingStatics")) {
            params.autoGenerateMissingStatics = configJ["AutoGenerateMissingStatics"].get<bool>();
        }
        if (configJ.contains("GeneratePbrSlots")) {
            params.generatePbrSlots = configJ["GeneratePbrSlots"].get<bool>();
        }
        if (configJ.contains("AutoGenerateAllowlist")) {
            params.autoGenerateAllowlist.clear();
            for (const auto& item : configJ["AutoGenerateAllowlist"]) {
                params.autoGenerateAllowlist.push_back(StringUtil::utf8toUTF16(item.get<string>()));
            }
        }
    } catch (const exception& e) {
        Logger::warn("Failed to parse settings file, using defaults: {}", e.what());
        return ABParams {};
    }

    return params;
}

auto ABConfig::toJson(const ABParams& params) -> nlohmann::json
{
    nlohmann::json configJ;

    configJ["UiLanguage"] = params.uiLanguage;
    configJ["UiTheme"] = params.uiTheme;
    configJ["GameLocation"] = StringUtil::utf16toUTF8(params.gameLocation);
    configJ["GameType"] = static_cast<int>(params.gameType);
    configJ["OutputLocation"] = StringUtil::utf16toUTF8(params.outputLocation);
    configJ["ModManager"] = static_cast<int>(params.modManager);
    configJ["Mo2InstancePath"] = StringUtil::utf16toUTF8(params.mo2InstancePath);
    configJ["Mo2ProfileName"] = StringUtil::utf16toUTF8(params.mo2ProfileName);

    configJ["LandscapeFolderRules"] = nlohmann::json::array();
    for (const auto& rule : params.landscapeFolderRules) {
        configJ["LandscapeFolderRules"].push_back({
            { "FolderName", StringUtil::utf16toUTF8(rule.folderName) },
            { "TypeLabel", StringUtil::utf16toUTF8(rule.typeLabel) },
        });
    }

    configJ["MeshBlacklist"] = nlohmann::json::array();
    for (const auto& item : params.meshBlacklist) {
        configJ["MeshBlacklist"].push_back(StringUtil::utf16toUTF8(item));
    }

    configJ["EditorIdBlacklistKeywords"] = nlohmann::json::array();
    for (const auto& item : params.editorIdBlacklistKeywords) {
        configJ["EditorIdBlacklistKeywords"].push_back(StringUtil::utf16toUTF8(item));
    }

    configJ["TextureSetNamingTemplate"] = StringUtil::utf16toUTF8(params.textureSetNamingTemplate);
    configJ["AutoGenerateMissingStatics"] = params.autoGenerateMissingStatics;
    configJ["GeneratePbrSlots"] = params.generatePbrSlots;

    configJ["AutoGenerateAllowlist"] = nlohmann::json::array();
    for (const auto& item : params.autoGenerateAllowlist) {
        configJ["AutoGenerateAllowlist"].push_back(StringUtil::utf16toUTF8(item));
    }

    return configJ;
}

void ABConfig::saveTo(const filesystem::path& configFilePath, const ABParams& params)
{
    try {
        const auto configJ = toJson(params);
        error_code ec;
        filesystem::create_directories(configFilePath.parent_path(), ec);

        ofstream file(configFilePath);
        if (!file.is_open()) {
            Logger::warn("Failed to save settings file");
            return;
        }

        file << configJ.dump(2);
    } catch (const exception& e) {
        // Mirrors loadFrom()'s own try/catch - a failure here (e.g. dump() rejecting invalid
        // UTF-8) should cost the user their settings for this run, not crash the app on "Start
        // Patching".
        Logger::warn("Failed to save settings file: {}", e.what());
    }
}
