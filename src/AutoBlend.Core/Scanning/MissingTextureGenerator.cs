using System.Diagnostics;
using System.Linq;

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
/// Implemented by shelling out to texconv.exe (Microsoft's DirectXTex CLI tool, MIT-licensed,
/// shipped alongside AutoBlend.Core.dll - see native/AutoBlend/CMakeLists.txt) with
/// `--swizzle rgb1`, which keeps R/G/B from the source and hardcodes A to 1 (fully opaque).
/// </summary>
public sealed class MissingTextureGenerator
{
    private readonly IGameFileProbe _fileProbe;
    private readonly string _outputLocation;
    private readonly string? _texconvPath;
    private readonly Dictionary<string, string?> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _diagnostics = new();
    private bool _reportedMissingTexconv;

    /// <summary>Number of textures successfully synthesized this run.</summary>
    public int GeneratedCount { get; private set; }

    /// <summary>Number of generation attempts that were made but failed (source not found,
    /// texconv failed to run, etc.) - distinct from a sibling already existing, which never
    /// reaches this generator at all.</summary>
    public int FailedCount { get; private set; }

    /// <summary>Human-readable detail for every failed attempt, capped to avoid flooding the run
    /// log on a modlist where texconv can't run at all - the first entry always explains why.</summary>
    public IReadOnlyList<string> Diagnostics => _diagnostics;

    public MissingTextureGenerator(IGameFileProbe fileProbe, string outputLocation, string? texconvPath)
    {
        _fileProbe = fileProbe;
        _outputLocation = outputLocation;
        _texconvPath = texconvPath;
    }

    /// <summary>
    /// Generates an opaque copy of <paramref name="sourceDiffusePath"/> (a path relative to Data,
    /// e.g. "textures\landscape\rocks01.dds") at <paramref name="targetRelativePath"/> (e.g.
    /// "textures\landscape\statics\rocks01.dds") inside the output location's own textures folder.
    /// Cached per target path within this generator's lifetime (one patch run) so repeat calls
    /// don't re-invoke texconv for the same texture. Returns false if generation isn't possible
    /// (texconv missing, source missing, or the conversion itself failed) - callers should treat
    /// that exactly like "no sibling found" rather than an error.
    /// </summary>
    public bool TryGenerate(string sourceDiffusePath, string targetRelativePath, out string generatedFullPath)
    {
        if (_cache.TryGetValue(targetRelativePath, out var cached))
        {
            generatedFullPath = cached ?? string.Empty;
            return cached is not null;
        }

        var success = TryGenerateCore(sourceDiffusePath, targetRelativePath, out generatedFullPath);
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

    private bool TryGenerateCore(string sourceDiffusePath, string targetRelativePath, out string generatedFullPath)
    {
        generatedFullPath = Path.Combine(_outputLocation, targetRelativePath);

        if (string.IsNullOrEmpty(_texconvPath) || !File.Exists(_texconvPath))
        {
            if (!_reportedMissingTexconv)
            {
                _reportedMissingTexconv = true;
                AddDiagnostic($"Auto-generation disabled: texconv.exe not found at '{_texconvPath}'.");
            }
            return false;
        }

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

            var psi = new ProcessStartInfo
            {
                FileName = _texconvPath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            psi.ArgumentList.Add("-ft");
            psi.ArgumentList.Add("dds");
            psi.ArgumentList.Add("--swizzle");
            psi.ArgumentList.Add("rgb1");
            psi.ArgumentList.Add("-y");
            psi.ArgumentList.Add("-o");
            psi.ArgumentList.Add(outputDir);
            psi.ArgumentList.Add(extractedPath);

            using var proc = Process.Start(psi);
            if (proc is null)
            {
                AddDiagnostic($"'{sourceDiffusePath}': failed to start texconv.exe.");
                return false;
            }

            var stdout = proc.StandardOutput.ReadToEnd();
            var stderr = proc.StandardError.ReadToEnd();
            proc.WaitForExit();

            // texconv names its output after the input file's own stem - the extracted temp file's
            // GUID name, not the final destination filename - so move it into place afterward.
            var texconvOutputPath = Path.Combine(outputDir, Path.GetFileNameWithoutExtension(extractedPath) + ".dds");
            if (proc.ExitCode != 0 || !File.Exists(texconvOutputPath))
            {
                var detail = !string.IsNullOrWhiteSpace(stderr) ? stderr : stdout;
                AddDiagnostic($"'{sourceDiffusePath}': texconv.exe exited {proc.ExitCode}: {detail.Trim()}");
                return false;
            }

            if (File.Exists(generatedFullPath))
            {
                File.Delete(generatedFullPath);
            }

            File.Move(texconvOutputPath, generatedFullPath);
            success = true;
            return true;
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

            // Directory.CreateDirectory above may have created an empty "statics"/"blending"
            // folder that a failed generation never populated - remove it so a run that can't
            // generate anything (e.g. texconv.exe missing its runtime DLLs) doesn't litter the
            // output textures tree with empty subfolders. Only touches directories this call
            // itself created, and only if still empty, so a folder another texture already
            // populated is never removed.
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
