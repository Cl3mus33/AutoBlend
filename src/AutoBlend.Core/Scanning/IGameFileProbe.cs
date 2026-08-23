namespace AutoBlend.Core.Scanning;

/// <summary>
/// Abstracts "does this file exist in the load order" over loose files and packed BSA/BA2
/// archives, so detection/patch logic doesn't need to know which one it's reading from.
/// </summary>
public interface IGameFileProbe : IDisposable
{
    /// <param name="relativeDataPath">Path relative to the game's Data folder, e.g. "textures\landscape\dirt02.dds".</param>
    bool Exists(string relativeDataPath);

    Stream OpenRead(string relativeDataPath);

    /// <summary>Lists every file matching <paramref name="extension"/> anywhere under
    /// <paramref name="relativeFolder"/> (recursive), across every source this probe knows about -
    /// used to search content (e.g. PBRNifPatcher json configs) rather than probe one specific
    /// path. Paths are relative to the game's Data folder, same convention as <see cref="Exists"/>.</summary>
    IEnumerable<string> EnumerateFiles(string relativeFolder, string extension);
}
