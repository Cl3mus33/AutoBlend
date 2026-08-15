#include "ABConfig.hpp"

#include "util/FileUtil.hpp"
#include "util/Logger.hpp"
#include "util/StringUtil.hpp"

#include <nlohmann/json.hpp>

#include <fstream>

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

auto ABConfig::getConfigPath() -> filesystem::path { return appDataDir() / "AutoBlend" / "settings.json"; }

auto ABConfig::load() -> ABParams { return loadFrom(getConfigPath()); }

void ABConfig::save(const ABParams& params) { saveTo(getConfigPath(), params); }

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
        }
        if (configJ.contains("MeshBlacklist")) {
            params.meshBlacklist.clear();
            for (const auto& item : configJ["MeshBlacklist"]) {
                params.meshBlacklist.push_back(StringUtil::utf8toUTF16(item.get<string>()));
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
