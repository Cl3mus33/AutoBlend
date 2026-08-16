using Mutagen.Bethesda;
using Mutagen.Bethesda.Archives;

namespace AutoBlend.Core.Scanning;

/// <summary>
/// Parses a Mod Organizer 2 profile's modlist.txt and resolves a relative Data-folder path
/// against its virtual file system: the "overwrite" folder first, then each enabled mod folder's
/// own loose files in priority order, then (only if no loose file anywhere provides it) each
/// enabled mod's own BSA/BA2 archives, also in priority order. MO2 writes modlist.txt in the
/// OPPOSITE order from its own mod list panel (file top = panel bottom) - verified directly
/// against a real instance's modlist.txt alongside a screenshot of its panel, four mods deep. The
/// panel's own bottom wins conflicts, so the file's own top is highest priority - the first
/// enabled line in the file is checked first here. Loose always beats archived, matching Skyrim's
/// own engine behavior - a mod that packs assets into its own BSA rather than shipping them loose
/// (e.g. Beyond Skyrim's BSAssets.bsa/BSHeartland.bsa) was previously invisible to this reader
/// entirely, since only loose files were ever checked.
/// </summary>
public sealed class Mo2InstanceReader : IDisposable
{
    /// <summary>The path the user gave us — MO2's own notion of "the instance" (where
    /// ModOrganizer.ini lives). For a "global" instance this is under %LOCALAPPDATA%\ModOrganizer\
    /// and does NOT necessarily contain mods/profiles/overwrite itself — see <see cref="DataRoot"/>.</summary>
    public string InstancePath { get; }

    /// <summary>Where mods/profiles/overwrite/downloads actually live. Equal to
    /// <see cref="InstancePath"/> unless ModOrganizer.ini sets a custom base_directory (common
    /// when mods are stored on a different drive than the instance's AppData metadata).</summary>
    public string DataRoot { get; }

    public string ModsRoot { get; }
    public string? OverwriteFolder { get; }
    public IReadOnlyList<string> EnabledModFoldersHighToLowPriority { get; }

    private readonly GameRelease _gameRelease;
    private IReadOnlyList<IArchiveReader>? _modArchiveReaders;
    private readonly Dictionary<IArchiveReader, Dictionary<string, IArchiveFile>> _archiveIndexes = new();

    public Mo2InstanceReader(string instancePath, string profileName, GameRelease gameRelease)
    {
        InstancePath = instancePath;
        _gameRelease = gameRelease;
        DataRoot = ResolveDataRoot(instancePath);
        ModsRoot = Path.Combine(DataRoot, "mods");

        var overwrite = Path.Combine(DataRoot, "overwrite");
        OverwriteFolder = Directory.Exists(overwrite) ? overwrite : null;

        var modlistPath = Path.Combine(DataRoot, "profiles", profileName, "modlist.txt");
        if (!File.Exists(modlistPath))
        {
            throw new FileNotFoundException(
                $"No modlist.txt found for profile '{profileName}'. Checked: '{modlistPath}'. " +
                $"MO2 Instance Path should be the folder MO2 itself calls the instance (containing ModOrganizer.ini) — " +
                $"if that instance uses a custom base directory, it's read automatically from ModOrganizer.ini.",
                modlistPath);
        }

        // ParseEnabledMods reads the file top-to-bottom, exactly the order it's written in. Verified
        // directly against a real instance's own modlist.txt alongside a screenshot of its MO2 mod
        // panel: the file is written in the OPPOSITE order from the panel (file top = panel bottom),
        // not the same order as an earlier version of this comment assumed. Since the panel's own
        // bottom wins conflicts, that makes the file's own TOP the highest-priority mod - so the
        // parsed top-to-bottom list already IS high-to-low priority, with no reversal needed.
        EnabledModFoldersHighToLowPriority = ParseEnabledMods(modlistPath)
            .Select(name => Path.Combine(ModsRoot, name))
            .Where(Directory.Exists)
            .ToList();
    }

    public string ProfilePath(string profileName) => Path.Combine(DataRoot, "profiles", profileName);

    /// <summary>Active plugin filenames (esp/esm/esl) from plugins.txt, in load-order — top to bottom of the file.</summary>
    public IReadOnlyList<string> ReadActivePlugins(string profileName)
    {
        var pluginsPath = Path.Combine(ProfilePath(profileName), "plugins.txt");
        var result = new List<string>();
        if (!File.Exists(pluginsPath))
        {
            return result;
        }

        foreach (var rawLine in File.ReadAllLines(pluginsPath))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#') || !line.StartsWith('*'))
            {
                continue;
            }

            result.Add(line[1..].Trim());
        }
        return result;
    }

    /// <summary>Loose-file resolution only (overwrite, then each enabled mod folder in priority
    /// order) - does NOT consult any mod's own BSA/BA2 archives. Used for plugins (.esp/.esm/.esl),
    /// which are never packed into archives, so archive-awareness would only add cost there. For
    /// meshes/textures/anything that CAN legitimately live in a mod's own archive, use
    /// <see cref="TryResolveLooseOrArchived"/> instead.</summary>
    public bool TryResolve(string relativeDataPath, out string fullPath)
    {
        if (OverwriteFolder is not null)
        {
            var overwritePath = Path.Combine(OverwriteFolder, relativeDataPath);
            if (File.Exists(overwritePath))
            {
                fullPath = overwritePath;
                return true;
            }
        }

        foreach (var modFolder in EnabledModFoldersHighToLowPriority)
        {
            var candidate = Path.Combine(modFolder, relativeDataPath);
            if (File.Exists(candidate))
            {
                fullPath = candidate;
                return true;
            }
        }

        fullPath = string.Empty;
        return false;
    }

    /// <summary>Same loose-file resolution as <see cref="TryResolve"/>, then - only if no mod
    /// provides a loose override - checks every enabled mod's own BSA/BA2 archives, also in
    /// priority order (matching Skyrim's own engine rule: loose always beats archived, regardless
    /// of which mod either comes from). A mod that ships assets packed into its own archive rather
    /// than loose (e.g. Beyond Skyrim's BSAssets.bsa) resolves through this path.</summary>
    public bool TryResolveLooseOrArchived(string relativeDataPath, out Stream stream)
    {
        if (TryResolve(relativeDataPath, out var loosePath))
        {
            stream = File.OpenRead(loosePath);
            return true;
        }

        foreach (var reader in GetOrBuildModArchiveReaders())
        {
            var index = GetOrBuildArchiveIndex(reader);
            if (index.TryGetValue(relativeDataPath, out var archiveFile))
            {
                stream = archiveFile.AsStream();
                return true;
            }
        }

        stream = Stream.Null;
        return false;
    }

    public bool ExistsLooseOrArchived(string relativeDataPath)
    {
        if (TryResolve(relativeDataPath, out _))
        {
            return true;
        }

        foreach (var reader in GetOrBuildModArchiveReaders())
        {
            if (GetOrBuildArchiveIndex(reader).ContainsKey(relativeDataPath))
            {
                return true;
            }
        }

        return false;
    }

    // Lazy and built once: a modlist can carry hundreds of mods, most without their own archives,
    // and each IArchiveReader.Files enumeration is a real cost - only pay it for instances that
    // actually construct this reader, and only once regardless of how many paths get queried.
    private IReadOnlyList<IArchiveReader> GetOrBuildModArchiveReaders()
    {
        if (_modArchiveReaders is not null)
        {
            return _modArchiveReaders;
        }

        var readers = new List<IArchiveReader>();
        foreach (var modFolder in EnabledModFoldersHighToLowPriority)
        {
            IEnumerable<string> archivePaths;
            try
            {
                archivePaths = Directory.EnumerateFiles(modFolder, "*.bsa")
                    .Concat(Directory.EnumerateFiles(modFolder, "*.ba2"));
            }
            catch (IOException)
            {
                continue;
            }

            foreach (var archivePath in archivePaths)
            {
                try
                {
                    readers.Add(Archive.CreateReader(_gameRelease, archivePath));
                }
                catch
                {
                    // Skip archives Mutagen can't parse (corrupt/unsupported format) rather than
                    // failing the whole run over one bad file.
                }
            }
        }

        _modArchiveReaders = readers;
        return readers;
    }

    private Dictionary<string, IArchiveFile> GetOrBuildArchiveIndex(IArchiveReader reader)
    {
        if (_archiveIndexes.TryGetValue(reader, out var existing))
        {
            return existing;
        }

        // Case-insensitive: real-world archives frequently mix casing between what a plugin's own
        // Model.File path uses and what got packed into the archive - see ArchiveAwareFileProbe's
        // own identical indexing for the same fix applied to the vanilla game's own BSAs.
        var index = new Dictionary<string, IArchiveFile>(StringComparer.OrdinalIgnoreCase);
        foreach (var archiveFile in reader.Files)
        {
            index.TryAdd(archiveFile.Path, archiveFile);
        }

        _archiveIndexes[reader] = index;
        return index;
    }

    public void Dispose()
    {
        if (_modArchiveReaders is null)
        {
            return;
        }

        foreach (var reader in _modArchiveReaders)
        {
            (reader as IDisposable)?.Dispose();
        }
    }

    /// <summary>
    /// MO2 stores each "global" instance's ModOrganizer.ini under %LOCALAPPDATA%\ModOrganizer\{name}\,
    /// which is what MO2's own UI calls "the instance" — but mods/profiles/overwrite only live
    /// directly inside that folder if the instance never set a custom base_directory (common when
    /// someone wants mods on a bigger/faster drive than the OS one). Read the ini's base_directory
    /// key when present and follow it; otherwise assume a portable instance where everything is
    /// co-located with ModOrganizer.ini.
    /// </summary>
    private static string ResolveDataRoot(string instancePath)
    {
        var iniPath = Path.Combine(instancePath, "ModOrganizer.ini");
        if (!File.Exists(iniPath))
        {
            return instancePath;
        }

        foreach (var rawLine in File.ReadAllLines(iniPath))
        {
            var line = rawLine.Trim();
            if (!line.StartsWith("base_directory=", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var value = line["base_directory=".Length..].Trim();
            if (!string.IsNullOrEmpty(value))
            {
                return value;
            }
        }

        return instancePath;
    }

    /// <summary>
    /// Reads ModOrganizer.ini's selected_profile (the profile MO2 itself currently has active,
    /// stored as "selected_profile=@ByteArray(Name)") so the UI can default to it instead of
    /// making every user type "Default" by hand. Returns false if the ini or key isn't found.
    /// </summary>
    public static bool TryDetectSelectedProfile(string instancePath, out string profileName)
    {
        var iniPath = Path.Combine(instancePath, "ModOrganizer.ini");
        if (File.Exists(iniPath))
        {
            foreach (var rawLine in File.ReadAllLines(iniPath))
            {
                var line = rawLine.Trim();
                if (!line.StartsWith("selected_profile=", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var value = line["selected_profile=".Length..].Trim();
                var start = value.IndexOf('(');
                var end = value.IndexOf(')');
                if (start >= 0 && end > start)
                {
                    value = value[(start + 1)..end];
                }

                if (!string.IsNullOrEmpty(value))
                {
                    profileName = value;
                    return true;
                }
            }
        }

        profileName = string.Empty;
        return false;
    }

    private static List<string> ParseEnabledMods(string modlistPath)
    {
        var result = new List<string>();
        foreach (var rawLine in File.ReadAllLines(modlistPath))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#') || !line.StartsWith('+'))
            {
                continue;
            }

            result.Add(line[1..].Trim());
        }
        return result;
    }
}
