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
        var derivedName = _namingTemplate
            .Replace("{Type}", detection.Rule.TypeLabel)
            .Replace("{Name}", source.SourceName);

        var derived = new TextureSet(_patchMod, derivedName)
        {
            // A freshly constructed TextureSet does not pre-populate its AssetLink slots - each
            // one must be assigned a new instance rather than mutated via .GivenPath on a
            // possibly-null existing reference.
            Diffuse = new AssetLink<SkyrimTextureAssetType>(detection.DerivedDiffusePath),
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

    private static AssetLink<SkyrimTextureAssetType> ToAssetLink(string? sourcePath) =>
        string.IsNullOrEmpty(sourcePath)
            ? new AssetLink<SkyrimTextureAssetType>()
            : new AssetLink<SkyrimTextureAssetType>(sourcePath);
}
