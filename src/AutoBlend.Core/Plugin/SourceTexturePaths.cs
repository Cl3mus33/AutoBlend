using Mutagen.Bethesda.Skyrim;

namespace AutoBlend.Core.Plugin;

/// <summary>
/// The 8 texture slot paths a shape is currently rendering with, regardless of where that
/// assignment actually lives: most landscape statics have no ESP-level TextureSet at all — the
/// paths are embedded directly in the NIF's shader property — while a shape already carrying an
/// Alternate Texture override from another mod has a real TXST record to read instead. Either
/// source normalizes to this same shape so <see cref="DerivedTextureSetFactory"/> doesn't need to
/// care which one it got.
/// </summary>
public sealed record SourceTexturePaths(
    string? Diffuse,
    string? NormalOrGloss,
    string? GlowOrDetailMap,
    string? Height,
    string? Environment,
    string? EnvironmentMaskOrSubsurfaceTint,
    string? Multilayer,
    string? BacklightMaskOrSpecular,
    string SourceName)
{
    public static SourceTexturePaths FromTextureSet(ITextureSetGetter txst) => new(
        Diffuse: txst.Diffuse?.GivenPath,
        NormalOrGloss: txst.NormalOrGloss?.GivenPath,
        GlowOrDetailMap: txst.GlowOrDetailMap?.GivenPath,
        Height: txst.Height?.GivenPath,
        Environment: txst.Environment?.GivenPath,
        EnvironmentMaskOrSubsurfaceTint: txst.EnvironmentMaskOrSubsurfaceTint?.GivenPath,
        Multilayer: txst.Multilayer?.GivenPath,
        BacklightMaskOrSpecular: txst.BacklightMaskOrSpecular?.GivenPath,
        SourceName: string.IsNullOrEmpty(txst.EditorID) ? txst.FormKey.ID.ToString() : txst.EditorID);
}
