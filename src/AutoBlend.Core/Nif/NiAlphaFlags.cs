namespace AutoBlend.Core.Nif;

/// <summary>
/// NiAlphaProperty source/dest blend function values, per the real nif.xml AlphaFunction
/// ordering (confirmed empirically against NifSkope — this is NOT the same ordering as modern
/// D3D blend enums, notably SrcAlpha/InvSrcAlpha sit at 6/7 here, not 4/5).
/// </summary>
public enum AlphaBlendFunction
{
    One = 0,
    Zero = 1,
    SrcColor = 2,
    InvSrcColor = 3,
    DstColor = 4,
    InvDstColor = 5,
    SrcAlpha = 6,
    InvSrcAlpha = 7,
    DstAlpha = 8,
    InvDstAlpha = 9,
    SrcAlphaSat = 10,
    BothSrcAlpha = 11,
    BothInvSrcAlpha = 12,
}

/// <summary>
/// Bit-level accessors for NiAlphaProperty.flags (u16). Layout per the niftools/pyffi nif.xml
/// reference: bit 0 alpha-blend enable, bits 1-4 source blend func, bits 5-8 dest blend func,
/// bit 9 alpha-test enable, bits 10-12 test func, bit 13 no-sorter (triangle sort disable).
/// Bits 14-15 are unused/reserved.
/// </summary>
public static class NiAlphaFlags
{
    private const int AlphaBlendEnableBit = 0;
    private const int SourceBlendShift = 1;
    private const int DestBlendShift = 5;
    private const int AlphaTestEnableBit = 9;
    private const int TestFunctionShift = 10;
    private const int NoSorterBit = 13;

    private const ushort FourBitMask = 0b1111;
    private const ushort ThreeBitMask = 0b111;

    public static bool IsAlphaBlendEnabled(ushort flags) => GetBit(flags, AlphaBlendEnableBit);
    public static bool IsAlphaTestEnabled(ushort flags) => GetBit(flags, AlphaTestEnableBit);
    public static bool IsNoSorterSet(ushort flags) => GetBit(flags, NoSorterBit);

    public static AlphaBlendFunction GetSourceBlend(ushort flags) =>
        (AlphaBlendFunction)((flags >> SourceBlendShift) & FourBitMask);

    public static AlphaBlendFunction GetDestBlend(ushort flags) =>
        (AlphaBlendFunction)((flags >> DestBlendShift) & FourBitMask);

    /// <summary>
    /// Returns flags with alpha testing disabled and alpha blending enabled using the standard
    /// "normal" blend equation (SrcAlpha / InvSrcAlpha), matching vanilla Skyrim landscape/foliage
    /// meshes that already blend. The test function/threshold bits are left untouched since they
    /// become inert once testing is disabled, and the no-sorter bit is preserved as-is.
    /// </summary>
    public static ushort ToAlphaBlending(ushort flags)
    {
        ushort result = flags;
        result = SetBit(result, AlphaBlendEnableBit, true);
        result = SetBit(result, AlphaTestEnableBit, false);
        result = SetBlendFunctionBits(result, SourceBlendShift, FourBitMask, (ushort)AlphaBlendFunction.SrcAlpha);
        result = SetBlendFunctionBits(result, DestBlendShift, FourBitMask, (ushort)AlphaBlendFunction.InvSrcAlpha);
        return result;
    }

    private static bool GetBit(ushort value, int bit) => ((value >> bit) & 1) != 0;

    private static ushort SetBit(ushort value, int bit, bool set)
    {
        var mask = (ushort)(1 << bit);
        return set ? (ushort)(value | mask) : (ushort)(value & ~mask);
    }

    private static ushort SetBlendFunctionBits(ushort value, int shift, ushort mask, ushort functionValue)
    {
        var cleared = (ushort)(value & ~(mask << shift));
        return (ushort)(cleared | ((functionValue & mask) << shift));
    }
}
