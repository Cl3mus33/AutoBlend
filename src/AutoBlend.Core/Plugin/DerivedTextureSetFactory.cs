using AutoBlend.Core.Configuration;
using Mutagen.Bethesda.Plugins.Assets;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Skyrim.Assets;

namespace AutoBlend.Core.Plugin;

/// <summary>
/// Creates a new TXST record in the patch mod carrying only the Diffuse (repointed to the
/// detected statics/blending variant) and Normal/Gloss slots — every other slot (environment
/// mask, glow/detail, height, environment, multilayer, backlight/specular) is deliberately left
/// unset. Copying the source's parallax/Complex Material/PBR-related slots forward would bake in
/// whatever that specific source happened to carry, which may not even apply to the detected
/// variant's own diffuse; staying vanilla-friendly (2 slots only) leaves that entirely to a
/// downstream run of a dedicated tool like PGPatcher, which decides parallax/CM/PBR per texture
/// on its own terms.
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

    public TextureSet CreateDerived(SourceTexturePaths source, LandscapeFolderRule rule, string derivedDiffusePath)
    {
        var derivedName = _namingTemplate
            .Replace("{Type}", rule.TypeLabel)
            .Replace("{Name}", source.SourceName);

        var derived = new TextureSet(_patchMod, derivedName)
        {
            // A freshly constructed TextureSet does not pre-populate its AssetLink slots — each
            // one must be assigned a new instance rather than mutated via .GivenPath on a
            // possibly-null existing reference.
            Diffuse = new AssetLink<SkyrimTextureAssetType>(derivedDiffusePath),
            NormalOrGloss = ToAssetLink(source.NormalOrGloss),
        };

        _patchMod.TextureSets.Add(derived);
        return derived;
    }

    private static AssetLink<SkyrimTextureAssetType> ToAssetLink(string? sourcePath) =>
        string.IsNullOrEmpty(sourcePath)
            ? new AssetLink<SkyrimTextureAssetType>()
            : new AssetLink<SkyrimTextureAssetType>(sourcePath);
}
