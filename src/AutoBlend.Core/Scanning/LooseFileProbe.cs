namespace AutoBlend.Core.Scanning;

/// <summary>
/// Loose-file-only <see cref="IGameFileProbe"/>, resolving paths against a Data folder root.
/// Real Skyrim load orders also carry BSA/BA2-packed content that this does not see on its own —
/// see <see cref="ArchiveAwareFileProbe"/>, which layers archive fallback on top of this.
/// </summary>
public sealed class LooseFileProbe : IGameFileProbe
{
    private readonly string _dataRoot;

    public LooseFileProbe(string dataRoot)
    {
        _dataRoot = dataRoot;
    }

    public bool Exists(string relativeDataPath) => File.Exists(ResolvePath(relativeDataPath));

    public Stream OpenRead(string relativeDataPath) => File.OpenRead(ResolvePath(relativeDataPath));

    public IEnumerable<string> EnumerateFiles(string relativeFolder, string extension)
    {
        var full = ResolvePath(relativeFolder);
        if (!Directory.Exists(full))
        {
            yield break;
        }

        foreach (var file in Directory.EnumerateFiles(full, "*" + extension, SearchOption.AllDirectories))
        {
            yield return Path.GetRelativePath(_dataRoot, file);
        }
    }

    private string ResolvePath(string relativeDataPath) => Path.Combine(_dataRoot, relativeDataPath);

    public void Dispose()
    {
        // no unmanaged resources held directly against loose files
    }
}
