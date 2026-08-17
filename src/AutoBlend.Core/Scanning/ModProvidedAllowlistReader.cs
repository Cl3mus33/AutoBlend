using System.Text.Json;

namespace AutoBlend.Core.Scanning;

/// <summary>
/// Mod authors can ship their own AutoGenerateAllowlist entries alongside their mod: a plain JSON
/// array of the same wildcard path strings <see cref="Configuration.PatcherSettings.AutoGenerateAllowlist"/>
/// already uses, in a file at the Data root named "*_autoblend.json" - mirroring Base Object
/// Swapper's own "*_SWAP.ini" convention (one uniquely-named file per mod, not a single shared
/// filename). Since every mod's file has its own name, several mods' manifests never collide and
/// all contribute - e.g. a landscape pack's own "Vanaheimr_autoblend.json" for its base textures
/// and a separate seasons add-on's own "VanaheimrSeasons_autoblend.json" for its own, read
/// together rather than one silently overwriting the other.
/// </summary>
public static class ModProvidedAllowlistReader
{
    private const string FileSuffix = "_autoblend.json";

    /// <param name="mo2Reader">
    /// When set, every enabled mod's own folder (plus the overwrite folder) is scanned
    /// individually for matching files - not just whatever's visible through a single merged/
    /// winning view - since the whole point is accumulating every mod's own contribution. Null
    /// (manual install/Vortex) falls back to the single merged Data folder, which already reflects
    /// whatever the last-installed mod with the same filename left behind - no different from how
    /// any other Data-root file behaves without MO2's per-mod folder separation.
    /// </param>
    public static IReadOnlyList<string> Collect(Mo2InstanceReader? mo2Reader, string dataFolder, List<string> warnings)
    {
        // Highest priority first so a same-named file from a higher-priority mod naturally wins if
        // two mods ever do collide on the exact same filename - same override semantics as any
        // other Data-root file.
        var filesToLoad = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (mo2Reader is not null)
        {
            var folders = new List<string>();
            if (mo2Reader.OverwriteFolder is not null)
            {
                folders.Add(mo2Reader.OverwriteFolder);
            }
            folders.AddRange(mo2Reader.EnabledModFoldersHighToLowPriority);

            foreach (var folder in folders)
            {
                CollectFrom(folder, filesToLoad);
            }
        }
        else
        {
            CollectFrom(dataFolder, filesToLoad);
        }

        var merged = new List<string>();
        foreach (var (name, path) in filesToLoad)
        {
            try
            {
                var json = File.ReadAllText(path);
                var entries = JsonSerializer.Deserialize<List<string>>(json);
                if (entries is not null)
                {
                    merged.AddRange(entries);
                }
            }
            catch (Exception ex)
            {
                warnings.Add($"Could not read mod-provided allowlist '{name}': {ex.GetType().Name}: {ex.Message}");
            }
        }

        return merged;
    }

    private static void CollectFrom(string folder, Dictionary<string, string> filesToLoad)
    {
        if (!Directory.Exists(folder))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(folder, "*" + FileSuffix, SearchOption.TopDirectoryOnly))
        {
            filesToLoad.TryAdd(Path.GetFileName(file), file);
        }
    }
}
