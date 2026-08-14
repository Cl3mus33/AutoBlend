#pragma once

#include <nlohmann/json_fwd.hpp>

#include <filesystem>
#include <string>
#include <vector>

/**
 * @brief Skyrim variant to patch. Values (and their on-wire order) must stay in lockstep with
 * AutoBlend.Core.Configuration.GameType - both sides serialize this as a plain integer.
 */
enum class ABGameType : int { SKYRIM_SE = 0, SKYRIM_LE = 1 };

/// @brief Mirrors AutoBlend.Core.Configuration.ModManagerType (same integer values).
enum class ABModManagerType : int { NONE = 0, MOD_ORGANIZER_2 = 1 };

/// @brief Mirrors AutoBlend.Core.Configuration.LandscapeFolderRule.
struct ABLandscapeFolderRule {
    std::wstring folderName;
    std::wstring typeLabel;
};

/**
 * @brief User-configurable parameters for AutoBlend, persisted as JSON. Field set and JSON key
 * names mirror AutoBlend.Core.Configuration.PatcherSettings exactly (PascalCase keys, default
 * System.Text.Json serialization - no naming policy) so both this native shell and the WPF app
 * read/write the very same %APPDATA%\AutoBlend\settings.json without any translation layer.
 */
struct ABParams {
    std::wstring gameLocation;
    ABGameType gameType = ABGameType::SKYRIM_SE;
    std::wstring outputLocation;

    ABModManagerType modManager = ABModManagerType::NONE;
    std::wstring mo2InstancePath;
    std::wstring mo2ProfileName = L"Default";

    std::vector<ABLandscapeFolderRule> landscapeFolderRules {
        { L"statics", L"Statics" },
        { L"blending", L"Blending" },
    };

    std::vector<std::wstring> meshBlacklist { LR"(*\glass\*)", LR"(*\ice\*)" };
    std::vector<std::wstring> editorIdBlacklistKeywords { L"ice", L"frozen", L"glass", L"unique" };

    std::wstring textureSetNamingTemplate = L"{Type}{Name}";
};

/**
 * @class ABConfig
 * @brief Loads/saves ABParams to the same settings.json AutoBlend.Core's SettingsService (C#)
 * reads and writes, so settings persist between runs and stay shared with the WPF app - no DNNE
 * round-trip needed just to open the settings window (matches AutoSeasons' own ASConfig, which
 * does its own file I/O rather than going through ASMutagen for this).
 */
class ABConfig {
public:
    static auto load() -> ABParams;
    static void save(const ABParams& params);

    /// @brief Serializes params to the exact JSON shape AutoBlend.Core.Configuration.PatcherSettings
    /// (de)serializes - used both by save() and to build the payload start_patch_run expects.
    static auto toJson(const ABParams& params) -> nlohmann::json;

private:
    static auto getConfigPath() -> std::filesystem::path;
};
