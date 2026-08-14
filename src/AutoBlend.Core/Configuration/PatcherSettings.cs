namespace AutoBlend.Core.Configuration;

/// <summary>
/// Full configuration for a patching run — mirrors the AutoSeasons settings window layout
/// (General/Options tabs, wildcard blocklist + EditorID keyword list) adapted to the
/// alpha-blending patch: instead of skipping seasonal variation, these lists skip meshes/records
/// where alpha testing is intentional and should not become alpha blending.
/// </summary>
public sealed class PatcherSettings
{
    public string GameLocation { get; set; } = string.Empty;
    public GameType GameType { get; set; } = GameType.SkyrimSE;
    public string OutputLocation { get; set; } = string.Empty;

    /// <summary>
    /// None covers both a manual install and Vortex's default deployment (files already merged
    /// into Data — nothing extra to configure). ModOrganizer2 requires <see cref="Mo2InstancePath"/>
    /// and <see cref="Mo2ProfileName"/> to reconstruct its virtual file system.
    /// </summary>
    public ModManagerType ModManager { get; set; } = ModManagerType.None;

    /// <summary>Root of the MO2 instance (the folder containing "mods", "profiles", "overwrite").</summary>
    public string Mo2InstancePath { get; set; } = string.Empty;

    public string Mo2ProfileName { get; set; } = "Default";

    /// <summary>
    /// Sub-folder names checked under any "*/landscape/" texture path (e.g. dlc01/landscape,
    /// mod-added paths). Presence of any one of these folders next to a landscape texture
    /// triggers the patch for meshes/records using that texture set.
    /// </summary>
    public List<LandscapeFolderRule> LandscapeFolderRules { get; set; } = LandscapeFolderRule.Defaults.ToList();

    /// <summary>
    /// Wildcard path patterns (e.g. "*\glass\*", "*\ice\*"). Meshes matching a rule here are
    /// skipped entirely, even if they would otherwise match the detection criteria.
    /// </summary>
    public List<string> MeshBlacklist { get; set; } = new()
    {
        @"*\glass\*",
        @"*\ice\*",
        @"*\trees\*",
        @"*\actors\*",
    };

    /// <summary>
    /// Case-insensitive substrings. Records whose EditorID contains one of these are skipped —
    /// useful for statics where alpha testing (crisp cutout) is intentional rather than an
    /// artifact of the vanilla parallax setup.
    /// </summary>
    public List<string> EditorIdBlacklistKeywords { get; set; } = new()
    {
        "ice",
        "frozen",
        "glass",
        "unique",
    };

    /// <summary>
    /// Template for generated TextureSet EditorIDs. Supported tokens: {Type} (resolved via the
    /// matching LandscapeFolderRule's TypeLabel) and {Name} (the source vanilla TXST EditorID).
    /// </summary>
    public string TextureSetNamingTemplate { get; set; } = "{Type}{Name}";
}
