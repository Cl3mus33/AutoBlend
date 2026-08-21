using System.Linq;
using System.Text.Json;

namespace AutoBlend.Core.Configuration;

public sealed class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    private readonly string _settingsPath;

    public SettingsService(string? settingsPath = null)
    {
        _settingsPath = settingsPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AutoBlend",
            "settings.json");
    }

    public PatcherSettings Load()
    {
        if (!File.Exists(_settingsPath))
        {
            return new PatcherSettings();
        }

        var json = File.ReadAllText(_settingsPath);
        var settings = JsonSerializer.Deserialize<PatcherSettings>(json, JsonOptions) ?? new PatcherSettings();

        // A settings.json saved before the "blend" rule was introduced has only its own two
        // entries (statics/blending) - deserialization replaces the list wholesale rather than
        // merging against LandscapeFolderRule.Defaults, so an existing user's file would otherwise
        // never pick up the new rule at all. Backfill it once here rather than requiring a manual
        // settings.json edit or a full reset.
        if (!settings.LandscapeFolderRules.Any(r => r.FolderName.Equals("blend", StringComparison.OrdinalIgnoreCase)))
        {
            settings.LandscapeFolderRules.Add(new LandscapeFolderRule("blend", "Blend"));
        }

        // Same reasoning as the "blend" rule backfill above: road-texture-replacer mods (Simplest
        // Roads, Simply Dirt Roads) reuse an ordinary landscape texture's diffuse on their own road
        // meshes, which AutoBlend can't distinguish from a real landscape mesh sharing that same
        // texture - producing malformed derived paths and wrong texture assignments on road meshes.
        // A settings.json saved before this fix needs it backfilled once too.
        if (!settings.MeshBlacklist.Any(p => p.Equals(@"*\roads\*", StringComparison.OrdinalIgnoreCase)))
        {
            settings.MeshBlacklist.Add(@"*\roads\*");
        }

        return settings;
    }

    public void Save(PatcherSettings settings)
    {
        var directory = Path.GetDirectoryName(_settingsPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(_settingsPath, json);
    }
}
