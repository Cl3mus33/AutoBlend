namespace AutoBlend.Core.Nif;

/// <summary>
/// Bit-level accessors for BSShaderProperty.shaderFlags2 (u32) — the "SLSF2" flags. Only the bits
/// this codebase actually touches are defined here; see the niftools/nifxml
/// SkyrimShaderPropertyFlags2 reference for the full set.
/// </summary>
public static class BSShaderFlags2
{
    // Both confirmed against niftools/nifxml's SkyrimShaderPropertyFlags2 enum.
    private const uint ZBufferWriteBit = 0x1; // value 1, bit 0
    private const uint NoFadeBit = 0x8; // value 8, bit 3 - disables distance-based dithered fading

    public static bool IsZBufferWriteSet(uint flags2) => (flags2 & ZBufferWriteBit) != 0;
    public static bool IsNoFadeSet(uint flags2) => (flags2 & NoFadeBit) != 0;

    public static uint WithZBufferWrite(uint flags2) => flags2 | ZBufferWriteBit;
    public static uint WithNoFade(uint flags2) => flags2 | NoFadeBit;
}
