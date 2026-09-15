using System.Text.Json;
using System.Text.Json.Serialization;

namespace AutoBlend.Core.Scanning;

/// <summary>
/// What a mod author wants AutoBlend to do with one specific mesh, overriding whatever the normal
/// statics/blending folder-detection heuristic would have decided for it.
/// </summary>
public enum MeshOverrideSetting
{
    /// <summary>No override - the normal heuristic decides, exactly as if this mesh weren't listed
    /// at all. Only meaningful as an explicit "don't touch this one, even though a future version
    /// of this file might default the rest to something else" - see <see cref="ModProvidedMeshConfigReader"/>'s
    /// own remarks on why there is no file-wide default.</summary>
    Default,

    /// <summary>Force this mesh's alpha-blend-eligible shape(s) into blend mode, even when neither
    /// an Alternate Texture nor the mesh's own embedded diffuse resolves to a detected
    /// statics/blending sibling. No texture is swapped or derived for a shape forced this way -
    /// only its NiAlphaProperty mode changes - since there is nothing to derive a TextureSet from
    /// without a real detected sibling. Intended for mod authors who have already hand-authored a
    /// mesh's own diffuse to look correct under alpha-blending and only need the technical flag
    /// flipped.</summary>
    Blend,

    /// <summary>Never touch this mesh, even if it would otherwise match the normal heuristic - the
    /// precise, single-mesh equivalent of a <see cref="Configuration.PatcherSettings.MeshBlacklist"/>
    /// wildcard entry, for a mod author who wants to exclude one specific file without reaching for
    /// (or shipping instructions for) a wildcard rule.</summary>
    Test,
}

/// <summary>
/// Mod authors can ship their own per-mesh overrides alongside their mod: a JSON array of
/// {mesh_Filepath, setting} entries in a file at the Data root named "*_autoblend_schema.json" -
/// same one-uniquely-named-file-per-mod convention as <see cref="ModProvidedAllowlistReader"/>'s
/// own "*_autoblend.json", so several mods' own files never collide and all contribute.
///
/// Deliberately has no file-wide default setting: a mod's own file only ever names the specific
/// meshes it wants to override, since a "default applies to every mesh this mod could possibly
/// touch" has no way to be resolved consistently once more than one mod's file is present (which
/// one's default wins for a mesh neither file explicitly lists?). Every entry is precise and
/// additive instead, the same override semantics every other Data-root file already has.
/// </summary>
public static class ModProvidedMeshConfigReader
{
    private const string FileSuffix = "_autoblend_schema.json";

    private sealed record MeshOverrideEntry(
        [property: JsonPropertyName("mesh_Filepath")] string? MeshFilepath,
        [property: JsonPropertyName("setting")] string? Setting);

    /// <param name="mo2Reader">Same reasoning as <see cref="ModProvidedAllowlistReader.Collect"/>:
    /// every enabled mod's own folder (plus the overwrite folder) is scanned individually under
    /// MO2, so every mod's own file contributes rather than only whichever one wins a merged
    /// view.</param>
    public static IReadOnlyDictionary<string, MeshOverrideSetting> Collect(Mo2InstanceReader? mo2Reader, string dataFolder, List<string> warnings)
    {
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

        var merged = new Dictionary<string, MeshOverrideSetting>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, path) in filesToLoad)
        {
            try
            {
                var json = File.ReadAllText(path);
                var entries = JsonSerializer.Deserialize<List<MeshOverrideEntry>>(json);
                if (entries is null)
                {
                    continue;
                }

                foreach (var entry in entries)
                {
                    if (string.IsNullOrWhiteSpace(entry.MeshFilepath))
                    {
                        warnings.Add($"Mesh config '{name}' has an entry with no mesh_Filepath - skipped.");
                        continue;
                    }

                    if (!Enum.TryParse<MeshOverrideSetting>(entry.Setting, ignoreCase: true, out var setting))
                    {
                        warnings.Add($"Mesh config '{name}' has an entry for '{entry.MeshFilepath}' with an unrecognized setting '{entry.Setting}' (expected Blend/Test/Default) - skipped.");
                        continue;
                    }

                    if (setting == MeshOverrideSetting.Default)
                    {
                        continue;
                    }

                    // First (highest-priority) file to mention a given mesh wins - same override
                    // semantics as any other Data-root file, and as ModProvidedAllowlistReader's
                    // own identical dictionary-based merge.
                    merged.TryAdd(NormalizeMeshPath(entry.MeshFilepath), setting);
                }
            }
            catch (Exception ex)
            {
                warnings.Add($"Could not read mod-provided mesh config '{name}': {ex.GetType().Name}: {ex.Message}");
            }
        }

        return merged;
    }

    // Mod authors may write either backslashes or forward slashes, and may or may not include the
    // leading "meshes\" - normalize to the same backslash-separated, "meshes\"-prefixed relative
    // path this project's own meshRelativeToData already uses everywhere else, so a lookup against
    // it is a plain case-insensitive dictionary hit rather than a bespoke comparison.
    private static string NormalizeMeshPath(string meshFilepath)
    {
        var normalized = meshFilepath.Replace('/', '\\').TrimStart('\\');
        return normalized.StartsWith("meshes\\", StringComparison.OrdinalIgnoreCase)
            ? normalized
            : Path.Combine("meshes", normalized);
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
