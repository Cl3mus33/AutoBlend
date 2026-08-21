namespace AutoBlend.Core.Configuration;

/// <summary>
/// Full configuration for a patching run — mirrors the AutoSeasons settings window layout
/// (General/Options tabs, wildcard blocklist + EditorID keyword list) adapted to the
/// alpha-blending patch: instead of skipping seasonal variation, these lists skip meshes/records
/// where alpha testing is intentional and should not become alpha blending.
/// </summary>
public sealed class PatcherSettings
{
    /// <summary>GUI language code (AutoBlend_translations/&lt;code&gt;.json, native shell only);
    /// English if missing. Rides along in the shared settings file purely so both shells could, in
    /// principle, agree on it - the WPF shell doesn't currently read this itself.</summary>
    public string UiLanguage { get; set; } = "en";

    /// <summary>GUI color theme (native shell only): "system", "light", or "dark".</summary>
    public string UiTheme { get; set; } = "system";

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
        @"*\roads\*",
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

    /// <summary>
    /// When true (default), a landscape texture with no existing statics sibling anywhere in the
    /// load order - AND matching <see cref="AutoGenerateAllowlist"/> - gets one synthesized on the
    /// fly (alpha channel stripped to opaque, see
    /// <see cref="AutoBlend.Core.Scanning.MissingTextureGenerator"/>) instead of being left
    /// unpatched, so texture authors no longer need to hand-author these siblings themselves.
    /// </summary>
    public bool AutoGenerateMissingStatics { get; set; } = true;

    /// <summary>
    /// Wildcard path patterns (matched against the source diffuse's own Data-relative path, e.g.
    /// "textures\landscape\dirt01.dds") gating which textures <see cref="AutoGenerateMissingStatics"/>
    /// is allowed to synthesize a statics sibling for - every landscape texture has structurally
    /// "eligible" alpha-blended shapes, but not every one is a texture a statics variant actually
    /// makes sense for, so generation only ever runs for entries explicitly listed here rather than
    /// blanket-covering anything eligible. Defaults to the base game/DLC/Beyond Skyrim landscape
    /// textures this project's own author has verified need one; add more (or wildcard patterns
    /// like "textures\mymod\landscape\*") for any other texture pack's own base landscape textures.
    /// </summary>
    /// <summary>
    /// When true, a derived TextureSet also carries forward the winning source TextureSet's own
    /// Height and EnvironmentMaskOrSubsurfaceTint (RMAOS) slots, not just Diffuse/Normal - so if a
    /// PBR texture pack is what's actually winning in the load order for a given texture, the
    /// derived "statics" TextureSet inherits its PBR slots too, instead of leaving them empty for a
    /// downstream tool to fill in. False (default) keeps the original vanilla-friendly behavior:
    /// only Diffuse and Normal/Gloss are ever set.
    /// </summary>
    public bool GeneratePbrSlots { get; set; }

    public List<string> AutoGenerateAllowlist { get; set; } = new()
    {
        @"textures\_resourcepack\landscape\desertrocks01_d.dds",
        @"textures\_resourcepack\landscape\sand01_d.dds",
        @"textures\_resourcepack\landscape\sand02_d.dds",
        @"textures\_resourcepack\landscape\volcaniccracked01_d.dds",
        @"textures\_resourcepack\landscape\volcanicrocks01_d.dds",
        @"textures\bscyrodiil\landscape\colovianrocks01revised.dds",
        @"textures\bscyrodiil\landscape\dirt01.dds",
        @"textures\bscyrodiil\landscape\oakforestgrass01.dds",
        @"textures\bscyrodiil\landscape\oakforestleaves01.dds",
        @"textures\bstamriel\landscape\westweald\rockswestweald.dds",
        @"textures\bstamriel\landscape\westweald\wwlemoss01.dds",
        @"textures\dlc02\landscape\volcanic_ash_01.dds",
        @"textures\dlc02\landscape\volcanic_ash_rocks_01_d.dds",
        @"textures\dlc02\landscape\volcanic_ash_tundra_02.dds",
        @"textures\dlc02\landscape\volcanic_ash_tundra_04.dds",
        @"textures\landscape\coastbeach01.dds",
        @"textures\landscape\dirt02.dds",
        @"textures\landscape\fallforestdirt01.dds",
        @"textures\landscape\fallforestgrass01.dds",
        @"textures\landscape\fallforestleaves01.dds",
        @"textures\landscape\fallforestrocks01.dds",
        @"textures\landscape\fieldgrass01.dds",
        @"textures\landscape\fieldgrass02.dds",
        @"textures\landscape\frozenmarshice01.dds",
        @"textures\landscape\mineralpoolterrace.dds",
        @"textures\landscape\reachmossyrocks01.dds",
        @"textures\landscape\riverbededge.dds",
        @"textures\landscape\rocks01.dds",
        @"textures\landscape\snow01.dds",
        @"textures\landscape\snowrocks01.dds",
        @"textures\landscape\tundra01.dds",
        @"textures\landscape\tundrarocks01.dds",
        @"textures\landscape\volcanictundradirt01.dds",
        @"textures\landscape\volcanictundragravel01.dds",
        @"textures\landscape\volcanictundraminerals01.dds",
        @"textures\landscape\volcanictundrarocks01.dds",
    };
}
