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
    /// @brief GUI language code (AutoBlend_translations/&lt;code&gt;.json); English if missing.
    std::string uiLanguage = "en";
    /// @brief GUI color theme: "system", "light", or "dark".
    std::string uiTheme = "system";

    std::wstring gameLocation;
    ABGameType gameType = ABGameType::SKYRIM_SE;
    std::wstring outputLocation;

    ABModManagerType modManager = ABModManagerType::NONE;
    std::wstring mo2InstancePath;
    std::wstring mo2ProfileName = L"Default";

    std::vector<ABLandscapeFolderRule> landscapeFolderRules {
        { L"statics", L"Statics" },
        { L"blending", L"Blending" },
        { L"blend", L"Blend" },
    };

    std::vector<std::wstring> meshBlacklist { LR"(*\glass\*)", LR"(*\ice\*)" };
    std::vector<std::wstring> editorIdBlacklistKeywords { L"ice", L"frozen", L"glass", L"unique" };

    std::wstring textureSetNamingTemplate = L"{Type}{Name}";

    /// @brief When true (default), a landscape texture with no existing statics sibling - and
    /// matching autoGenerateAllowlist - gets one synthesized on the fly (see
    /// AutoBlend.Core.Scanning.MissingTextureGenerator) instead of being left unpatched.
    bool autoGenerateMissingStatics = true;

    /// @brief When true, a derived TextureSet also carries forward the winning source
    /// TextureSet's own Height and EnvironmentMaskOrSubsurfaceTint (RMAOS) slots - Skyrim's 4-slot
    /// PBR convention (Diffuse/Normal/Height/RMAOS) - not just Diffuse/Normal. Correct when a PBR
    /// texture pack is what's actually winning in the load order for a given texture, so its own
    /// PBR maps carry through to the derived "statics" TextureSet instead of being left for a
    /// downstream tool to fill in. False (default) keeps the original vanilla-friendly behavior.
    bool generatePbrSlots = false;

    /// @brief Wildcard patterns (e.g. "textures\landscape\dirt01.dds", or
    /// "textures\mymod\landscape\*") gating which source diffuse textures autoGenerateMissingStatics
    /// is allowed to synthesize a statics sibling for - every landscape texture with an
    /// alpha-blended shape is structurally "eligible", but not every one is something a statics
    /// variant actually makes sense for, so generation is opt-in per texture. Empty by default -
    /// the base game/DLC/Beyond Skyrim landscape textures this project's own author originally
    /// hand-verified now ship as bundled "Vanilla_autoblend.json"/"BSBruma_autoblend.json"/
    /// "ResourcePack_autoblend.json" files next to the exe instead, picked up through the exact
    /// same mechanism as any other mod's own manifest rather than hardcoded here.
    std::vector<std::wstring> autoGenerateAllowlist {};
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

    /// @brief Loads params from an arbitrary JSON file, e.g. a user-picked profile via "Load
    /// Config...". Lets one shared AutoBlend install (outside any one modlist) keep separate
    /// settings per use case via saved JSON files, instead of relying on a dedicated per-modlist
    /// copy of the exe for isolation - mirrors PGPatcher's/AutoSeasons' own load/save pattern.
    static auto loadFrom(const std::filesystem::path& configFilePath) -> ABParams;
    /// @brief Saves params to an arbitrary JSON file, e.g. via "Save Config As...".
    static void saveTo(const std::filesystem::path& configFilePath, const ABParams& params);

    /// @brief Serializes params to the exact JSON shape AutoBlend.Core.Configuration.PatcherSettings
    /// (de)serializes - used both by save()/saveTo() and to build the payload start_patch_run expects.
    static auto toJson(const ABParams& params) -> nlohmann::json;

private:
    static auto getConfigPath() -> std::filesystem::path;
};
