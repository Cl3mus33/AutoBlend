#include <d3d11.h>
#include <DirectXTex.h>

#include <wrl/client.h>

using namespace DirectX;
using Microsoft::WRL::ComPtr;

namespace {
// Real-world landscape texture packs (e.g. Vanaheimr's 4K AIO variants) made the CPU BC7 encoder -
// even with PARALLEL|BC7_QUICK below - take 15-25s per texture; on a modlist needing dozens of
// generations that added minutes to every run. DirectXTex's DirectCompute-based GPU compressor
// (Compress(ID3D11Device*, ...)) does the same BC7 encode in a fraction of the time. Device
// creation is lazy and cached for this DLL's lifetime (one compression call per texconv-style
// invocation would otherwise pay device setup cost repeatedly); falls back to the CPU path
// automatically if no device is available (headless/CI machine, broken driver) or GPU compression
// itself fails for any reason, so this DLL still works everywhere the pure-CPU version did.
auto getSharedD3D11Device() -> ID3D11Device*
{
    static const ComPtr<ID3D11Device> device = [] {
        ComPtr<ID3D11Device> result;
        const D3D_DRIVER_TYPE driverTypes[] = { D3D_DRIVER_TYPE_HARDWARE, D3D_DRIVER_TYPE_WARP };
        for (const auto driverType : driverTypes) {
            if (SUCCEEDED(D3D11CreateDevice(
                    nullptr, driverType, nullptr, 0, nullptr, 0, D3D11_SDK_VERSION, &result, nullptr, nullptr))) {
                return result;
            }
        }
        return ComPtr<ID3D11Device> {};
    }();
    return device.Get();
}

// Skyrim's landscape alpha-blending reads its blend weight from the diffuse texture's own alpha
// channel, so forcing every pixel's alpha to fully opaque - while leaving RGB untouched - is what
// turns a blend-edge diffuse into a standalone "statics" one (verified against Vanaheimr's own
// hand-authored statics textures: identical RGB, only alpha differs). This mirrors texconv's own
// `--swizzle rgb1`, minus the swizzle CLI's own file I/O.
auto stripAlphaToOpaque(const wchar_t* srcPath, const wchar_t* dstPath, bool isPbr) -> int
{
    TexMetadata srcMetadata {};
    ScratchImage srcImage;
    if (FAILED(LoadFromDDSFile(srcPath, DDS_FLAGS_NONE, &srcMetadata, srcImage))) {
        return 1;
    }

    const Image* images = srcImage.GetImages();
    size_t imageCount = srcImage.GetImageCount();
    TexMetadata workingMetadata = srcMetadata;

    // Block-compressed formats (BC1/BC3/BC7 etc.) can't be edited pixel-by-pixel directly - the
    // alpha bits are packed per 4x4 block alongside color data, not a separate plane - so this
    // decompresses to plain RGBA8 first and recompresses afterward.
    ScratchImage decompressed;
    if (IsCompressed(srcMetadata.format)) {
        if (FAILED(Decompress(images, imageCount, srcMetadata, DXGI_FORMAT_R8G8B8A8_UNORM, decompressed))) {
            return 2;
        }
        images = decompressed.GetImages();
        imageCount = decompressed.GetImageCount();
        workingMetadata = decompressed.GetMetadata();
    }

    ScratchImage opaqueImage;
    const HRESULT transformHr = TransformImage(
        images, imageCount, workingMetadata,
        [](XMVECTOR* outPixels, const XMVECTOR* inPixels, size_t width, size_t /*y*/) {
            for (size_t x = 0; x < width; ++x) {
                outPixels[x] = XMVectorSetW(inPixels[x], 1.0f);
            }
        },
        opaqueImage);
    if (FAILED(transformHr)) {
        return 3;
    }

    // Regardless of what format the winning source happened to be in (BC1/BC3/BC7/uncompressed all
    // occur across a real modlist), recompress to ONE consistent target format - but which one
    // depends on what the generated texture is FOR, not just "Skyrim SE's preferred diffuse
    // format": a PBR diffuse needs BC1, sRGB-tagged (NOT the linear/UNORM-without-gamma-tag a bare
    // BC1_UNORM would produce) - Skyrim's PBR pipeline reads it that way specifically. A vanilla/
    // complex-material diffuse stays BC7 as before. Reported directly and confirmed against a real
    // PBR texture pack's own generated statics variant rendering wrong until this distinction
    // existed.
    const DXGI_FORMAT targetFormat = isPbr ? DXGI_FORMAT_BC1_UNORM_SRGB : DXGI_FORMAT_BC7_UNORM;

    ScratchImage recompressed;
    HRESULT compressHr = E_FAIL;

    // GPU path first (DirectCompute-based, dramatically faster for BC7 in particular - see the
    // getSharedD3D11Device() comment above for why; BC1 is cheap either way but the same device is
    // reused). alphaWeight 1.0 is DirectXTex's own documented "typical value" for BC7 - Compress()
    // ignores it for non-BC6H/BC7 targets, so passing it unconditionally for BC1 is harmless.
    if (auto* device = getSharedD3D11Device(); device != nullptr) {
        compressHr = Compress(device, opaqueImage.GetImages(), opaqueImage.GetImageCount(), opaqueImage.GetMetadata(),
            targetFormat, TEX_COMPRESS_DEFAULT, 1.0f, recompressed);
    }

    if (FAILED(compressHr)) {
        // CPU fallback: TEX_COMPRESS_DEFAULT does NOT use multithreading and, for BC7, runs an
        // exhaustive partition search - measured taking 15-25s for a single 4K landscape texture.
        // PARALLEL enables DirectXTex's own OpenMP-based multithreading (already a build
        // dependency - see CMakeLists.txt's vcomp140.dll bundling); BC7_QUICK swaps the exhaustive
        // search for BC7's minimal-mode encoder - only meaningful for BC7 itself, but harmless to
        // pass for BC1 (ignored). Output is a synthesized fallback texture (RGB already went
        // through one lossy decompress/recompress round-trip regardless), so the modest quality
        // trade for far less compute is the right call.
        const auto compressFlags = static_cast<TEX_COMPRESS_FLAGS>(TEX_COMPRESS_PARALLEL | TEX_COMPRESS_BC7_QUICK);
        compressHr = Compress(opaqueImage.GetImages(), opaqueImage.GetImageCount(), opaqueImage.GetMetadata(),
            targetFormat, compressFlags, TEX_THRESHOLD_DEFAULT, recompressed);
    }

    if (FAILED(compressHr)) {
        return 4;
    }

    if (FAILED(SaveToDDSFile(
            recompressed.GetImages(), recompressed.GetImageCount(), recompressed.GetMetadata(), DDS_FLAGS_NONE, dstPath))) {
        return 5;
    }

    return 0;
}
}

/**
 * @brief Writes an opaque (alpha-stripped) copy of the DDS at srcPath to dstPath, recompressed to
 * BC1 sRGB when isPbr is nonzero, or BC7 otherwise - see stripAlphaToOpaque's own comment for why
 * this can't just "preserve the original format" the way it used to. Returns 0 on success,
 * non-zero on failure - never throws across the P/Invoke boundary, matching the same "nothing here
 * can escape" convention AutoBlend.NativeExport's own DNNE exports use for the opposite
 * (native-calls-into-C#) direction.
 *
 * Called in-process via P/Invoke from AutoBlend.Core.Scanning.MissingTextureGenerator rather than
 * shelling out to texconv.exe as a subprocess: MO2's USVFS hooks CreateProcess for every child
 * process of an MO2-launched executable, and texconv.exe reliably crashed with
 * STATUS_ACCESS_VIOLATION under that hook in real-world testing despite working flawlessly
 * standalone outside MO2. Loading this as a library into the already-running (already-hooked)
 * process sidesteps that hook entirely, since no new process is ever created.
 */
extern "C" __declspec(dllexport) int __stdcall ab_strip_alpha_to_opaque(const wchar_t* srcPath, const wchar_t* dstPath, int isPbr)
{
    if (srcPath == nullptr || dstPath == nullptr) {
        return -1;
    }

    try {
        return stripAlphaToOpaque(srcPath, dstPath, isPbr != 0);
    } catch (...) {
        return -2;
    }
}
