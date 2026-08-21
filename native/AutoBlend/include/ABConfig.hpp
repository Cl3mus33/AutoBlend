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

    std::vector<std::wstring> meshBlacklist { LR"(*\glass\*)", LR"(*\ice\*)", LR"(*\roads\*)" };
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
    /// variant actually makes sense for, so generation is opt-in per texture. Defaults to the base
    /// game/DLC/Beyond Skyrim landscape textures this project's own author has verified need one.
    std::vector<std::wstring> autoGenerateAllowlist {
        LR"(textures\_resourcepack\landscape\desertrocks01_d.dds)",
        LR"(textures\_resourcepack\landscape\sand01_d.dds)",
        LR"(textures\_resourcepack\landscape\sand02_d.dds)",
        LR"(textures\_resourcepack\landscape\volcaniccracked01_d.dds)",
        LR"(textures\_resourcepack\landscape\volcanicrocks01_d.dds)",
        LR"(textures\bscyrodiil\landscape\colovianrocks01revised.dds)",
        LR"(textures\bscyrodiil\landscape\dirt01.dds)",
        LR"(textures\bscyrodiil\landscape\oakforestgrass01.dds)",
        LR"(textures\bscyrodiil\landscape\oakforestleaves01.dds)",
        LR"(textures\bstamriel\landscape\westweald\rockswestweald.dds)",
        LR"(textures\bstamriel\landscape\westweald\wwlemoss01.dds)",
        LR"(textures\dlc02\landscape\volcanic_ash_01.dds)",
        LR"(textures\dlc02\landscape\volcanic_ash_rocks_01_d.dds)",
        LR"(textures\dlc02\landscape\volcanic_ash_tundra_02.dds)",
        LR"(textures\dlc02\landscape\volcanic_ash_tundra_04.dds)",
        LR"(textures\landscape\coastbeach01.dds)",
        LR"(textures\landscape\dirt02.dds)",
        LR"(textures\landscape\fallforestdirt01.dds)",
        LR"(textures\landscape\fallforestgrass01.dds)",
        LR"(textures\landscape\fallforestleaves01.dds)",
        LR"(textures\landscape\fallforestrocks01.dds)",
        LR"(textures\landscape\fieldgrass01.dds)",
        LR"(textures\landscape\fieldgrass02.dds)",
        LR"(textures\landscape\frozenmarshice01.dds)",
        LR"(textures\landscape\mineralpoolterrace.dds)",
        LR"(textures\landscape\reachmossyrocks01.dds)",
        LR"(textures\landscape\riverbededge.dds)",
        LR"(textures\landscape\rocks01.dds)",
        LR"(textures\landscape\snow01.dds)",
        LR"(textures\landscape\snowrocks01.dds)",
        LR"(textures\landscape\tundra01.dds)",
        LR"(textures\landscape\tundrarocks01.dds)",
        LR"(textures\landscape\volcanictundradirt01.dds)",
        LR"(textures\landscape\volcanictundragravel01.dds)",
        LR"(textures\landscape\volcanictundraminerals01.dds)",
        LR"(textures\landscape\volcanictundrarocks01.dds)",
    };
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
    /// @brief Loads settings.json from beside the running exe (exeDir) - each modlist's own
    /// dedicated AutoBlend copy (the real, established deployment convention: a separate "AutoBlend"
    /// mod folder per MO2 instance) then keeps its own settings automatically, with no cross-modlist
    /// collision and nothing for the user to save/load by hand. Falls back to defaults if that
    /// file doesn't exist yet (first run for this copy).
    static auto load(const std::filesystem::path& exeDir) -> ABParams;
    /// @brief Saves settings.json beside the running exe (exeDir) - see load().
    static void save(const std::filesystem::path& exeDir, const ABParams& params);

    /// @brief Loads params from an arbitrary JSON file, e.g. a user-picked profile via "Load
    /// Config...". Still useful for named presets within one modlist (e.g. different output
    /// configurations to switch between), even though per-modlist isolation is now automatic.
    static auto loadFrom(const std::filesystem::path& configFilePath) -> ABParams;
    /// @brief Saves params to an arbitrary JSON file, e.g. via "Save Config As...".
    static void saveTo(const std::filesystem::path& configFilePath, const ABParams& params);

    /// @brief Serializes params to the exact JSON shape AutoBlend.Core.Configuration.PatcherSettings
    /// (de)serializes - used both by save()/saveTo() and to build the payload start_patch_run expects.
    static auto toJson(const ABParams& params) -> nlohmann::json;

private:
    static auto getConfigPath(const std::filesystem::path& exeDir) -> std::filesystem::path;
};
