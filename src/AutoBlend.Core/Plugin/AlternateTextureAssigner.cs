using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
using Noggog;

namespace AutoBlend.Core.Plugin;

/// <summary>
/// Adds or updates a Model.AlternateTextures entry so a specific NiTriShape (by name/3D index)
/// renders with a derived TextureSet, without touching any other shape on the same model.
/// </summary>
public static class AlternateTextureAssigner
{
    public static void Assign(Model model, string shapeName, int shapeIndex, ITextureSetGetter textureSet)
    {
        // A freshly overridden Model's AlternateTextures list is not pre-initialized — same
        // "not auto-populated" pattern as the AssetLink slots on TextureSet.
        model.AlternateTextures ??= new ExtendedList<AlternateTexture>();

        var existing = model.AlternateTextures.FirstOrDefault(a => a.Name == shapeName);
        if (existing is not null)
        {
            existing.NewTexture = new FormLink<ITextureSetGetter>(textureSet.FormKey);
            existing.Index = shapeIndex;
            return;
        }

        model.AlternateTextures.Add(new AlternateTexture
        {
            Name = shapeName,
            Index = shapeIndex,
            NewTexture = new FormLink<ITextureSetGetter>(textureSet.FormKey),
        });
    }
}
