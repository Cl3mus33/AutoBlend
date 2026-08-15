using AutoBlend.Core.Configuration;
using Mutagen.Bethesda.Plugins.Assets;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Skyrim.Assets;

namespace AutoBlend.Core.Plugin;

/// <summary>
/// Creates a new TXST record in the patch mod carrying the Diffuse (repointed to the detected
/// statics/blending variant) and Normal/Gloss slots, plus - when <see cref="_generatePbrSlots"/>
/// is set - Height and EnvironmentMaskOrSubsurfaceTint (RMAOS), Skyrim's 4-slot PBR convention
/// (Diffuse/Normal/Height/RMAOS). The remaining slots (glow/detail, environment, multilayer,
/// backlight/specular) are always left unset regardless. Copying the source's own slots forward
/// bakes in whatever that specific winning source happened to carry - correct exactly when a PBR
/// texture pack is what's actually winning in the load order for this texture, since then
/// source's own Height/RMAOS already point at that pack's own PBR maps. With the flag off (the
/// default), staying vanilla-friendly (2 slots only) leaves parallax/Complex Material/PBR
/// entirely to a downstream run of a dedicated tool like PGPatcher, which decides per texture on
/// its own terms.
/// </summary>
public sealed class DerivedTextureSetFactory
{
    private readonly ISkyrimMod _patchMod;
    private readonly string _namingTemplate;
    private readonly bool _generatePbrSlots;

    public DerivedTextureSetFactory(ISkyrimMod patchMod, string namingTemplate, bool generatePbrSlots = false)
    {
        _patchMod = patchMod;
        _namingTemplate = namingTemplate;
        _generatePbrSlots = generatePbrSlots;
    }

    public TextureSet CreateDerived(SourceTexturePaths source, LandscapeFolderRule rule, string derivedDiffusePath)
    {
        var derivedName = _namingTemplate
            .Replace("{Type}", rule.TypeLabel)
            .Replace("{Name}", source.SourceName);

        var derived = new TextureSet(_patchMod, derivedName)
        {
            // A freshly constructed TextureSet does not pre-populate its AssetLink slots - each
            // one must be assigned a new instance rather than mutated via .GivenPath on a
            // possibly-null existing reference.
            Diffuse = new AssetLink<SkyrimTextureAssetType>(derivedDiffusePath),
            NormalOrGloss = ToAssetLink(source.NormalOrGloss),
        };

        if (_generatePbrSlots)
        {
            derived.Height = ToAssetLink(source.Height);
            derived.EnvironmentMaskOrSubsurfaceTint = ToAssetLink(source.EnvironmentMaskOrSubsurfaceTint);
        }

        _patchMod.TextureSets.Add(derived);
        return derived;
    }

    private static AssetLink<SkyrimTextureAssetType> ToAssetLink(string? sourcePath) =>
        string.IsNullOrEmpty(sourcePath)
            ? new AssetLink<SkyrimTextureAssetType>()
            : new AssetLink<SkyrimTextureAssetType>(sourcePath);
}
