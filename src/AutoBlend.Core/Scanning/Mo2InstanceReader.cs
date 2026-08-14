namespace AutoBlend.Core.Scanning;

/// <summary>
/// Parses a Mod Organizer 2 profile's modlist.txt and resolves a relative Data-folder path
/// against its virtual file system: the "overwrite" folder first, then each enabled mod folder
/// in priority order. MO2 writes modlist.txt top-to-bottom matching its mod list panel, where the
/// bottom of that panel wins file conflicts — so the last enabled line in the file is checked
/// first here.
/// </summary>
public sealed class Mo2InstanceReader
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

    public Mo2InstanceReader(string instancePath, string profileName)
    {
        InstancePath = instancePath;
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

        var enabledLowToHighPriority = ParseEnabledMods(modlistPath);

        EnabledModFoldersHighToLowPriority = enabledLowToHighPriority
            .AsEnumerable()
            .Reverse()
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
