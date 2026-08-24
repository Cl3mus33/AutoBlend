using System.Linq;
using AutoBlend.Core.Scanning;
using Mutagen.Bethesda.Plugins.Assets;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Skyrim.Assets;

namespace AutoBlend.Core.Plugin;

/// <summary>
/// Creates a new TXST record in the patch mod carrying the Diffuse (repointed to the detected
/// statics/blending variant) and Normal/Gloss slots, plus - whenever
/// <see cref="LandscapeFolderDetection"/> resolved a PBR sibling for this texture (see
/// LandscapeFolderDetector's own generatePbrSlots) - Height and EnvironmentMaskOrSubsurfaceTint
/// (RMAOS), Skyrim's 4-slot PBR convention (Diffuse/Normal/Height/RMAOS). The remaining slots
/// (glow/detail, environment, multilayer, backlight/specular) are always left unset. Detection
/// already resolved whether a PBR sibling exists at all (and its own Normal/Height/RMAOS, per
/// Skyrim's "_n"/"_p"/"_rmaos" suffix convention) - this factory just wires that data onto the
/// TXST record, so its own PBR-fields being null is exactly "nothing to add" (PBR generation off,
/// or no PBR sibling found for this specific texture) and needs no separate flag here.
/// </summary>
public sealed class DerivedTextureSetFactory
{
    private readonly ISkyrimMod _patchMod;
    private readonly string _namingTemplate;

    public DerivedTextureSetFactory(ISkyrimMod patchMod, string namingTemplate)
    {
        _patchMod = patchMod;
        _namingTemplate = namingTemplate;
    }

    public TextureSet CreateDerived(SourceTexturePaths source, LandscapeFolderDetection detection)
    {
        // Named after the actual resolved TEXTURE (e.g. "Rocks01", from
        // VanillaDerivedDiffusePath's own filename), not source.SourceName - which, for a
        // BaseDerived shape (see PatchOrchestrator.ShapeTreatmentKind), is the NIF SHAPE's own
        // name, not the texture's identity. A shape's own name can be completely unrelated to what
        // texture it happens to use (a decorative rock sub-object nifly auto-named "RockPileM01:8"
        // inside some unrelated static mesh, rendering with a totally different "Rocks01" texture)
        // - reported directly as a derived TextureSet/PBRTextureSets json named "BlendRockPileM01"
        // for a shape that has nothing to do with any "RockPileM01" texture at all. The texture's
        // own filename is also what nifly auto-renames to disambiguate shapes (e.g. "RockPileM01:8",
        // "MountainTrim01_Rocks:0 - L2_Rocks:0") can never be - it's a clean file basename, no
        // colons/spaces/dashes to sanitize away. Still strips a leading, already-present type label
        // (e.g. a texture pack's own file already named "StaticsRocks01.dds") so the template's own
        // "{Type}" doesn't double it into "StaticsStaticsRocks01" - reported directly for that case.
        var textureBaseName = Path.GetFileNameWithoutExtension(detection.VanillaDerivedDiffusePath);
        var effectiveSourceName = textureBaseName.StartsWith(detection.Rule.TypeLabel, StringComparison.OrdinalIgnoreCase)
            ? textureBaseName[detection.Rule.TypeLabel.Length..]
            : textureBaseName;

        // Sanitized as a last resort (letters/digits/underscore only) in case some texture pack's
        // own filename still carries something non-conventional - keeps the EditorID CK-safe and
        // anything derived from it (e.g. MissingTextureGenerator.TryMirrorPbrTextureSetJson's own
        // output filename) filesystem-safe, without depending on every texture pack's own file
        // naming being clean.
        var derivedName = SanitizeEditorId(_namingTemplate
            .Replace("{Type}", detection.Rule.TypeLabel)
            .Replace("{Name}", effectiveSourceName));

        var derived = new TextureSet(_patchMod, derivedName)
        {
            // A freshly constructed TextureSet does not pre-populate its AssetLink slots - each
            // one must be assigned a new instance rather than mutated via .GivenPath on a
            // possibly-null existing reference.
            Diffuse = ToAssetLink(detection.DerivedDiffusePath),
            NormalOrGloss = ToAssetLink(detection.PbrNormalPath ?? source.NormalOrGloss),
            // Height/EnvironmentMaskOrSubsurfaceTint previously ONLY ever came from PBR detection,
            // with no fallback to the source TextureSet's own values the way NormalOrGloss already
            // had - silently dropping both whenever generatePbrSlots was off or no PBR sibling was
            // found. Reported as purple/broken textures downstream of a complex-material patcher:
            // verified directly that even vanilla Skyrim's own "Landscape\Dirt02.dds" TXST record
            // already populates both (Dirt02_p.dds/Dirt02_m.dds - complex material shipped in the
            // base game itself), so this dropped real, commonly-populated data on almost every
            // derived TextureSet, not just PBR/complex-material texture packs specifically.
            Height = ToAssetLink(detection.PbrHeightPath ?? source.Height),
            EnvironmentMaskOrSubsurfaceTint = ToAssetLink(detection.PbrRmaosPath ?? source.EnvironmentMaskOrSubsurfaceTint),
        };

        _patchMod.TextureSets.Add(derived);
        return derived;
    }

    /// <summary>
    /// A TextureSet's own AssetLink&lt;SkyrimTextureAssetType&gt; slots are always relative to
    /// "Data\textures\", never carrying that segment themselves - confirmed directly against a real
    /// vanilla TXST record's own Normal/Gloss field ("DLC02\Landscape\volcanic_ash_rocks_01_n.dds",
    /// no "textures\" anywhere). Every path this factory works with, though, DOES carry a leading
    /// "textures\" - LandscapeFolderDetection's own DerivedDiffusePath/PbrNormalPath/PbrHeightPath/
    /// PbrRmaosPath always do (that's the convention used everywhere else, e.g. the file probe,
    /// which needs the full Data-relative path), and so does source.NormalOrGloss/Height/
    /// EnvironmentMaskOrSubsurfaceTint whenever it came from a mesh's own EMBEDDED NIF slots
    /// (BaseDerived - NifShapeTextureResolver reads the raw shader texture path, which the game
    /// itself stores WITH "textures\"); only when source's own fields came from an EXISTING TXST
    /// record's own GivenPath (AltTexDerived) are they already correctly prefix-less. Passing the
    /// prefixed form straight into an AssetLink constructor doesn't error, doesn't fail validation,
    /// and even displays as if the path were reasonable in a plugin editor - it just silently
    /// resolves to a doubled, nonexistent "Data\textures\textures\..." path in game, exactly the
    /// missing-texture ("purple") failure mode this whole area has been chasing all session.
    /// Stripped once here, unconditionally, for every field this factory ever assigns - a no-op for
    /// a path that's already correctly prefix-less.
    /// </summary>
    private static AssetLink<SkyrimTextureAssetType> ToAssetLink(string? sourcePath)
    {
        if (string.IsNullOrEmpty(sourcePath))
        {
            return new AssetLink<SkyrimTextureAssetType>();
        }

        const string prefix = "textures";
        var segments = sourcePath.Replace('/', '\\').Split('\\', StringSplitOptions.RemoveEmptyEntries).ToList();
        if (segments.Count > 1 && segments[0].Equals(prefix, StringComparison.OrdinalIgnoreCase))
        {
            segments.RemoveAt(0);
        }

        return new AssetLink<SkyrimTextureAssetType>(string.Join('\\', segments));
    }

    /// <summary>Strips everything but letters/digits/underscore - see the call site's own comment
    /// for why a raw NIF shape name can't be used as an EditorID (or filename derived from it)
    /// as-is.</summary>
    private static string SanitizeEditorId(string name) =>
        new(name.Where(c => char.IsLetterOrDigit(c) || c == '_').ToArray());
}
