namespace AutoBlend.Core.Configuration;

/// <summary>
/// Vortex (default hardlink/symlink-into-Data deployment) needs no special handling — the game's
/// Data folder already reflects the merged modlist as real files, same as a manual/vanilla
/// install. Mod Organizer 2 keeps mods in separate folders behind a virtual file system, so
/// reading its instance directly (profile + mod priority order) is required to see them.
/// </summary>
public enum ModManagerType
{
    None,
    ModOrganizer2,
}
