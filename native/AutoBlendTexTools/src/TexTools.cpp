#include <DirectXTex.h>

using namespace DirectX;

namespace {
// Skyrim's landscape alpha-blending reads its blend weight from the diffuse texture's own alpha
// channel, so forcing every pixel's alpha to fully opaque - while leaving RGB untouched - is what
// turns a blend-edge diffuse into a standalone "statics" one (verified against Vanaheimr's own
// hand-authored statics textures: identical RGB, only alpha differs). This mirrors texconv's own
// `--swizzle rgb1`, minus the swizzle CLI's own file I/O.
auto stripAlphaToOpaque(const wchar_t* srcPath, const wchar_t* dstPath) -> int
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
    // decompresses to plain RGBA8 first and recompresses back to the original format afterward.
    const bool wasCompressed = IsCompressed(srcMetadata.format);
    ScratchImage decompressed;
    if (wasCompressed) {
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

    const Image* finalImages = opaqueImage.GetImages();
    size_t finalImageCount = opaqueImage.GetImageCount();
    TexMetadata finalMetadata = opaqueImage.GetMetadata();

    ScratchImage recompressed;
    if (wasCompressed) {
        // TEX_COMPRESS_DEFAULT does NOT use multithreading and, for BC7, runs an exhaustive
        // partition search - measured taking minutes for a single 2048x2048 landscape texture.
        // PARALLEL enables DirectXTex's own OpenMP-based multithreading (already a build
        // dependency - see CMakeLists.txt's vcomp140.dll bundling); BC7_QUICK swaps the exhaustive
        // search for BC7's minimal-mode encoder. Output is a synthesized fallback texture (RGB
        // already went through one lossy decompress/recompress round-trip regardless), so the
        // modest quality trade for multiple orders of magnitude less compute is the right call.
        const auto compressFlags = static_cast<TEX_COMPRESS_FLAGS>(TEX_COMPRESS_PARALLEL | TEX_COMPRESS_BC7_QUICK);
        if (FAILED(Compress(
                opaqueImage.GetImages(), opaqueImage.GetImageCount(), opaqueImage.GetMetadata(),
                srcMetadata.format, compressFlags, TEX_THRESHOLD_DEFAULT, recompressed))) {
            return 4;
        }
        finalImages = recompressed.GetImages();
        finalImageCount = recompressed.GetImageCount();
        finalMetadata = recompressed.GetMetadata();
    }

    if (FAILED(SaveToDDSFile(finalImages, finalImageCount, finalMetadata, DDS_FLAGS_NONE, dstPath))) {
        return 5;
    }

    return 0;
}
}

/**
 * @brief Writes an opaque (alpha-stripped) copy of the DDS at srcPath to dstPath, preserving its
 * original pixel format. Returns 0 on success, non-zero on failure - never throws across the
 * P/Invoke boundary, matching the same "nothing here can escape" convention
 * AutoBlend.NativeExport's own DNNE exports use for the opposite (native-calls-into-C#) direction.
 *
 * Called in-process via P/Invoke from AutoBlend.Core.Scanning.MissingTextureGenerator rather than
 * shelling out to texconv.exe as a subprocess: MO2's USVFS hooks CreateProcess for every child
 * process of an MO2-launched executable, and texconv.exe reliably crashed with
 * STATUS_ACCESS_VIOLATION under that hook in real-world testing despite working flawlessly
 * standalone outside MO2. Loading this as a library into the already-running (already-hooked)
 * process sidesteps that hook entirely, since no new process is ever created.
 */
extern "C" __declspec(dllexport) int __stdcall ab_strip_alpha_to_opaque(const wchar_t* srcPath, const wchar_t* dstPath)
{
    if (srcPath == nullptr || dstPath == nullptr) {
        return -1;
    }

    try {
        return stripAlphaToOpaque(srcPath, dstPath);
    } catch (...) {
        return -2;
    }
}
