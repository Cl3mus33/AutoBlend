using nifly;

namespace AutoBlend.Core.Nif;

/// <summary>
/// nifly can't open or write a file whose path has any non-ASCII character (reproduced directly with
/// a Cyrillic folder + file name: Load fails outright) - its Windows path handling goes through a
/// narrow-string API. Non-ASCII paths are common in practice (a Cyrillic/accented Windows user name
/// puts %TEMP% itself out of reach, and so can a mod manager's mods folder), so every NIF read/write
/// in this project goes through here: ASCII paths take the direct route, anything else is copied
/// through a temp file that is guaranteed to have an ASCII path.
/// </summary>
public static class NifIo
{
    private static readonly object _rootLock = new();
    private static string? _asciiTempRoot;

    public static int Load(NifFile nifFile, string path)
    {
        if (IsAscii(path))
        {
            return nifFile.Load(path);
        }

        var tempPath = NewAsciiTempFilePath();
        try
        {
            File.Copy(path, tempPath, overwrite: true);
            return nifFile.Load(tempPath);
        }
        finally
        {
            TryDelete(tempPath);
        }
    }

    public static int Save(NifFile nifFile, string path, NifSaveOptions options)
    {
        if (IsAscii(path))
        {
            return nifFile.Save(path, options);
        }

        var tempPath = NewAsciiTempFilePath();
        try
        {
            var result = nifFile.Save(tempPath, options);
            if (result == 0)
            {
                File.Copy(tempPath, path, overwrite: true);
            }

            return result;
        }
        finally
        {
            TryDelete(tempPath);
        }
    }

    /// <summary>
    /// A writable temp folder whose full path is pure ASCII: the normal temp folder when it already
    /// is, otherwise the first of a few fixed fallbacks that is.
    /// </summary>
    public static string GetAsciiTempRoot()
    {
        lock (_rootLock)
        {
            if (_asciiTempRoot is not null)
            {
                return _asciiTempRoot;
            }

            var candidates = new[]
            {
                Path.GetTempPath(),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "AutoBlend", "tmp"),
                @"C:\Users\Public\AutoBlend\tmp",
                Path.Combine(Path.GetPathRoot(Environment.SystemDirectory) ?? @"C:\", "AutoBlendTemp"),
            };

            foreach (var candidate in candidates)
            {
                if (!IsAscii(candidate))
                {
                    continue;
                }

                try
                {
                    Directory.CreateDirectory(candidate);
                    var probe = Path.Combine(candidate, Guid.NewGuid().ToString("N") + ".probe");
                    File.WriteAllText(probe, "x");
                    File.Delete(probe);
                    _asciiTempRoot = candidate;
                    return candidate;
                }
                catch (Exception)
                {
                    // Not writable - try the next fallback.
                }
            }

            throw new IOException("Could not find a writable temp folder with an ASCII-only path (needed to read/write NIF files).");
        }
    }

    public static bool IsAscii(string value)
    {
        foreach (var c in value)
        {
            if (c > 127)
            {
                return false;
            }
        }

        return true;
    }

    private static string NewAsciiTempFilePath() =>
        Path.Combine(GetAsciiTempRoot(), "nif_" + Guid.NewGuid().ToString("N") + ".nif");

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception)
        {
            // Best-effort cleanup of a scratch file.
        }
    }
}
