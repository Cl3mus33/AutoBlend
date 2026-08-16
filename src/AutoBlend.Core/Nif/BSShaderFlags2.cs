namespace AutoBlend.Core.Nif;

/// <summary>
/// Bit-level accessors for BSShaderProperty.shaderFlags2 (u32) — the "SLSF2" flags. Only the one
/// bit this codebase actually touches is defined here; see the niftools/nifxml
/// SkyrimShaderPropertyFlags2 reference for the full set.
/// </summary>
public static class BSShaderFlags2
{
    // Confirmed against niftools/nifxml's SkyrimShaderPropertyFlags2 enum (value 1, i.e. bit 0).
    private const uint ZBufferWriteBit = 0x1;

    public static bool IsZBufferWriteSet(uint flags2) => (flags2 & ZBufferWriteBit) != 0;

    public static uint WithZBufferWrite(uint flags2) => flags2 | ZBufferWriteBit;
}
