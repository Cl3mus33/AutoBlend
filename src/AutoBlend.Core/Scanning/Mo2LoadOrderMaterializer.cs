using Mutagen.Bethesda.Plugins;

namespace AutoBlend.Core.Scanning;

/// <summary>
/// Mutagen's GameEnvironment resolves every plugin from one physical Data folder — it has no
/// concept of MO2's per-mod folders. This copies just the active plugins.txt entries (resolved
/// through MO2's overwrite/mod-priority order, falling back to the vanilla Data folder for
/// Skyrim.esm and the DLCs) into a throwaway folder, in plugins.txt's load order, so
/// GameEnvironment can be pointed at something it understands. Meshes/textures are NOT
/// materialized here — those stay served live through <see cref="Mo2ModlistFileProbe"/>, this is
/// only for the ESP/ESM/ESL records themselves.
/// </summary>
public sealed class Mo2LoadOrderMaterializer
{
    // MO2 never writes these to plugins.txt - they're the game's own base masters, which can't be
    // disabled, so MO2's UI doesn't even show a toggle for them. Skipping this list would silently
    // build an environment missing Skyrim.esm itself (and the official DLCs), so every base-game
    // record - the vast majority of what a landscape-texture patcher cares about - would appear to
    // not exist at all, while records legitimately overriding it in mod plugins still "resolve" in
    // a way that looks superficially fine, making the gap easy to miss.
    //
    // _ResourcePack.esl is the same story: Anniversary Edition's shared asset container (rock/sand/
    // landscape textures used by several cc* content packs, with EditorIDs like "RP_..."), force-
    // loaded by the game engine itself and never listed in plugins.txt, same as the five above -
    // confirmed missing here caused every Alternate Texture pointing at one of its own TextureSets
    // to fail resolution ("Could not resolve existing TextureSet..." warnings) even though the
    // referenced texture files themselves were found and auto-generated just fine, since file
    // resolution and record resolution are entirely separate code paths.
    // Shared with PatchOrchestrator's own non-MO2 active-plugin-list construction - the same six
    // files are implicit there too, for the exact same reason.
    internal static readonly string[] ImplicitBaseMasterFileNames =
    {
        "Skyrim.esm",
        "Update.esm",
        "Dawnguard.esm",
        "HearthFires.esm",
        "Dragonborn.esm",
        "_ResourcePack.esl",
    };

    public sealed class MaterializedLoadOrder : IDisposable
    {
        public string DataFolder { get; }
        public IReadOnlyList<ModKey> LoadOrder { get; }

        public MaterializedLoadOrder(string dataFolder, IReadOnlyList<ModKey> loadOrder)
        {
            DataFolder = dataFolder;
            LoadOrder = loadOrder;
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(DataFolder, recursive: true);
            }
            catch
            {
                // best-effort cleanup of the temp merged-plugins folder
            }
        }
    }

    public static MaterializedLoadOrder Materialize(Mo2InstanceReader reader, string profileName, string vanillaDataFolder, List<string> warnings)
    {
        var activePlugins = reader.ReadActivePlugins(profileName);
        var tempFolder = Path.Combine(Path.GetTempPath(), "AutoBlend_LoadOrder_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempFolder);

        // Base masters go first, in their fixed canonical order, and only once - plugins.txt
        // normally never lists them, but if a given setup unusually does, don't double them up.
        var alreadyListed = new HashSet<string>(activePlugins, StringComparer.OrdinalIgnoreCase);
        var orderedPluginNames = ImplicitBaseMasterFileNames
            .Where(name => !alreadyListed.Contains(name))
            .Concat(activePlugins)
            .ToList();

        var loadOrder = new List<ModKey>();
        foreach (var pluginFileName in orderedPluginNames)
        {
            string? sourcePath = null;
            if (reader.TryResolve(pluginFileName, out var modResolvedPath))
            {
                sourcePath = modResolvedPath;
            }
            else
            {
                var vanillaPath = Path.Combine(vanillaDataFolder, pluginFileName);
                if (File.Exists(vanillaPath))
                {
                    sourcePath = vanillaPath;
                }
            }

            if (sourcePath is null)
            {
                warnings.Add($"Active plugin listed in plugins.txt but not found anywhere, skipped: {pluginFileName}");
                continue;
            }

            var destPath = Path.Combine(tempFolder, pluginFileName);
            File.Copy(sourcePath, destPath, overwrite: true);
            loadOrder.Add(ModKey.FromNameAndExtension(pluginFileName));
        }

        return new MaterializedLoadOrder(tempFolder, loadOrder);
    }
}
