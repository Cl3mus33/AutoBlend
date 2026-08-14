using AutoBlend.Core.Configuration;

namespace AutoBlend.Core.Scanning;

/// <summary>
/// Layers a Mod Organizer 2 instance's virtual file system without needing to actually run
/// through MO2, using <see cref="Mo2InstanceReader"/> for the overwrite/mod-priority resolution,
/// and falling back to the real game Data folder (loose + BSA/BA2) for anything no mod provides.
/// </summary>
public sealed class Mo2ModlistFileProbe : IGameFileProbe
{
    private readonly Mo2InstanceReader _reader;
    private readonly ArchiveAwareFileProbe _vanillaProbe;

    public Mo2ModlistFileProbe(Mo2InstanceReader reader, string dataFolder, GameType gameType)
    {
        _reader = reader;
        _vanillaProbe = new ArchiveAwareFileProbe(dataFolder, gameType);
    }

    public bool Exists(string relativeDataPath) =>
        _reader.TryResolve(relativeDataPath, out _) || _vanillaProbe.Exists(relativeDataPath);

    public Stream OpenRead(string relativeDataPath) =>
        _reader.TryResolve(relativeDataPath, out var fullPath) ? File.OpenRead(fullPath) : _vanillaProbe.OpenRead(relativeDataPath);

    public void Dispose() => _vanillaProbe.Dispose();
}
