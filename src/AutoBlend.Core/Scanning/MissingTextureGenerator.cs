using System.Linq;
using System.Runtime.InteropServices;

namespace AutoBlend.Core.Scanning;

/// <summary>
/// Synthesizes a missing statics/blending sibling texture on the fly from whatever diffuse is
/// actually winning in the load order, so a texture author never has to hand-author these siblings
/// themselves - AutoBlend covers the gap automatically.
///
/// The transform is a straight alpha-channel strip: Skyrim's landscape alpha-blending reads its
/// blend weight from the diffuse texture's own alpha channel (not vertex colors), so forcing that
/// channel to fully opaque - while leaving every RGB pixel untouched - is exactly what turns a
/// blend-edge diffuse into a standalone "statics" one. This was verified directly against
/// Vanaheimr's own hand-authored "landscape\statics\rocks01.dds": sampling both against the
/// original blended "landscape\rocks01.dds" showed identical RGB data (nothing but ordinary BC7
/// recompression noise, a handful of intensity levels out of 255) and the same near-255 flattened
/// alpha - i.e. this is the exact transform Vanaheimr's own textures were made with, not a
/// convenient approximation.
///
/// Implemented via a direct P/Invoke call into AutoBlendTexTools.dll (native/AutoBlendTexTools, a
/// small DirectXTex wrapper built alongside this project) rather than shelling out to texconv.exe
/// as a subprocess: MO2's USVFS hooks CreateProcess for every child process of an MO2-launched
/// executable, and texconv.exe reliably crashed with STATUS_ACCESS_VIOLATION under that hook in
/// real-world testing despite working flawlessly standalone outside MO2. Calling the same
/// transform in-process (no new process ever gets created) sidesteps that hook entirely.
/// </summary>
public sealed class MissingTextureGenerator
{
    [DllImport("AutoBlendTexTools.dll", CharSet = CharSet.Unicode)]
    private static extern int ab_strip_alpha_to_opaque(string srcPath, string dstPath, int isPbr);

    private readonly IGameFileProbe _fileProbe;
    private readonly string _outputLocation;
    private readonly Dictionary<string, string?> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _diagnostics = new();
    private bool _reportedMissingDll;

    /// <summary>Number of textures successfully synthesized this run.</summary>
    public int GeneratedCount { get; private set; }

    /// <summary>Number of generation attempts that were made but failed (source not found,
    /// transform failed, etc.) - distinct from a sibling already existing, which never reaches
    /// this generator at all.</summary>
    public int FailedCount { get; private set; }

    /// <summary>Human-readable detail for every failed attempt, capped to avoid flooding the run
    /// log on a modlist where generation can't run at all - the first entry always explains why.</summary>
    public IReadOnlyList<string> Diagnostics => _diagnostics;

    public MissingTextureGenerator(IGameFileProbe fileProbe, string outputLocation)
    {
        _fileProbe = fileProbe;
        _outputLocation = outputLocation;
    }

    /// <summary>
    /// Generates an opaque copy of <paramref name="sourceDiffusePath"/> (a path relative to Data,
    /// e.g. "textures\landscape\rocks01.dds") at <paramref name="targetRelativePath"/> (e.g.
    /// "textures\landscape\statics\rocks01.dds") inside the output location's own textures folder.
    /// <paramref name="isPbr"/> selects the output compression: PBR diffuse textures need BC1 (sRGB,
    /// not linear) while vanilla/complex-material ones need BC7 - recompressing a PBR source to BC7
    /// (or dropping its sRGB tag) reported directly as visibly wrong rendering downstream. Cached
    /// per target path within this generator's lifetime (one patch run) so repeat calls don't redo
    /// the transform for the same texture. Returns false if generation isn't possible (source
    /// missing, or the transform itself failed) - callers should treat that exactly like "no
    /// sibling found" rather than an error.
    /// </summary>
    public bool TryGenerate(string sourceDiffusePath, string targetRelativePath, bool isPbr, out string generatedFullPath)
    {
        if (_cache.TryGetValue(targetRelativePath, out var cached))
        {
            generatedFullPath = cached ?? string.Empty;
            return cached is not null;
        }

        var success = TryGenerateCore(sourceDiffusePath, targetRelativePath, isPbr, out generatedFullPath);
        _cache[targetRelativePath] = success ? generatedFullPath : null;
        if (success)
        {
            GeneratedCount++;
        }
        else
        {
            FailedCount++;
        }
        return success;
    }

    private void AddDiagnostic(string message)
    {
        // Capped so a modlist where every single texture fails the same way doesn't produce a
        // run log thousands of lines long - the first handful is always enough to diagnose.
        if (_diagnostics.Count < 20)
        {
            _diagnostics.Add(message);
        }
    }

    private bool TryGenerateCore(string sourceDiffusePath, string targetRelativePath, bool isPbr, out string generatedFullPath)
    {
        generatedFullPath = Path.Combine(_outputLocation, targetRelativePath);

        string extractedPath;
        try
        {
            using var source = _fileProbe.OpenRead(sourceDiffusePath);
            extractedPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".dds");
            using var dest = File.Create(extractedPath);
            source.CopyTo(dest);
        }
        catch (FileNotFoundException)
        {
            AddDiagnostic($"'{sourceDiffusePath}': source texture not found in the load order, skipped.");
            return false;
        }
        catch (Exception ex)
        {
            AddDiagnostic($"'{sourceDiffusePath}': failed to extract source texture ({ex.GetType().Name}: {ex.Message}).");
            return false;
        }

        var outputDir = Path.GetDirectoryName(generatedFullPath)!;
        var createdOutputDir = !Directory.Exists(outputDir);
        var success = false;

        try
        {
            Directory.CreateDirectory(outputDir);

            var resultCode = ab_strip_alpha_to_opaque(extractedPath, generatedFullPath, isPbr ? 1 : 0);
            if (resultCode != 0)
            {
                AddDiagnostic($"'{sourceDiffusePath}': AutoBlendTexTools failed (code {resultCode}).");
                return false;
            }

            success = true;
            return true;
        }
        catch (DllNotFoundException ex)
        {
            if (!_reportedMissingDll)
            {
                _reportedMissingDll = true;
                AddDiagnostic($"Auto-generation disabled: AutoBlendTexTools.dll not found ({ex.Message}).");
            }
            return false;
        }
        catch (Exception ex)
        {
            AddDiagnostic($"'{sourceDiffusePath}': {ex.GetType().Name}: {ex.Message}");
            return false;
        }
        finally
        {
            try
            {
                File.Delete(extractedPath);
            }
            catch
            {
                // best-effort cleanup of the temp extraction
            }

            // Directory.CreateDirectory above may have created an empty "statics" folder that a
            // failed generation never populated - remove it so a run that can't generate anything
            // doesn't litter the output textures tree with empty subfolders. Only touches
            // directories this call itself created, and only if still empty, so a folder another
            // texture already populated is never removed.
            if (!success && createdOutputDir)
            {
                try
                {
                    if (Directory.Exists(outputDir) && !Directory.EnumerateFileSystemEntries(outputDir).Any())
                    {
                        Directory.Delete(outputDir);
                    }
                }
                catch
                {
                    // best-effort cleanup
                }
            }
        }
    }
}
