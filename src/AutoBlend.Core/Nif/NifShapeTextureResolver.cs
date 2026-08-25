using nifly;

namespace AutoBlend.Core.Nif;

/// <summary>
/// Resolves the diffuse texture path embedded directly in a NIF shape (slot 0), used to know
/// which vanilla TextureSet a shape is rendering with before any ESP-level Alternate Texture
/// override is considered.
/// </summary>
public static class NifShapeTextureResolver
{
    // BSShaderTextureSet slot order — fixed across SE/AE, matches the TXST record's TX00-TX07
    // subrecord order 1:1.
    private const uint DiffuseSlot = 0;
    private const uint NormalOrGlossSlot = 1;
    private const uint GlowOrDetailMapSlot = 2;
    private const uint HeightSlot = 3;
    private const uint EnvironmentSlot = 4;
    private const uint EnvironmentMaskOrSubsurfaceTintSlot = 5;
    private const uint MultilayerSlot = 6;
    private const uint BacklightMaskOrSpecularSlot = 7;

    /// <summary>
    /// Reads all 8 embedded texture slots for a shape. Used when a shape has no ESP-level
    /// Alternate Texture override to derive from instead.
    /// </summary>
    public static Plugin.SourceTexturePaths GetAllSlots(NifFile nifFile, NiShape shape) => new(
        Diffuse: GetSlot(nifFile, shape, DiffuseSlot),
        NormalOrGloss: GetSlot(nifFile, shape, NormalOrGlossSlot),
        GlowOrDetailMap: GetSlot(nifFile, shape, GlowOrDetailMapSlot),
        Height: GetSlot(nifFile, shape, HeightSlot),
        Environment: GetSlot(nifFile, shape, EnvironmentSlot),
        EnvironmentMaskOrSubsurfaceTint: GetSlot(nifFile, shape, EnvironmentMaskOrSubsurfaceTintSlot),
        Multilayer: GetSlot(nifFile, shape, MultilayerSlot),
        BacklightMaskOrSpecular: GetSlot(nifFile, shape, BacklightMaskOrSpecularSlot),
        SourceName: shape.name.get());

    private static string? GetSlot(NifFile nifFile, NiShape shape, uint slot)
    {
        var path = nifFile.GetTexturePathByIndex(shape, slot);
        return string.IsNullOrEmpty(path) ? null : path;
    }
}
