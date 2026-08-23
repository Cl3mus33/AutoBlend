using AutoBlend.Core.Configuration;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Archives;

namespace AutoBlend.Core.Scanning;

/// <summary>
/// Loose files first (matching Skyrim's real VFS priority — loose always wins), falling back to
/// every BSA/BA2 applicable to the data folder for this game release. Each archive's full file
/// list is indexed case-insensitively on first use (not up front in the constructor) so a modlist
/// carrying many large archives only pays the indexing cost for archives we actually end up
/// querying.
/// </summary>
public sealed class ArchiveAwareFileProbe : IGameFileProbe
{
    private readonly LooseFileProbe _looseProbe;
    private readonly IReadOnlyList<IArchiveReader> _archiveReaders;

    // IArchiveReader.TryGetFolder(folderPath) does a case-SENSITIVE lookup internally - real-world
    // BSA/BA2 archives frequently mix casing between what a plugin's own Model.File path uses
    // (e.g. "BSCyrodiil\...") and what got packed into the archive (e.g. "bscyrodiil\..."), or even
    // between different records referencing the very same folder. Relying on TryGetFolder meant
    // roughly half of every such mismatched-case folder's files silently "did not exist" as far as
    // this probe was concerned - on one real modlist this was 91% of every "mesh not found"
    // warning. Indexing every archive's full file list once (case-insensitively, lazily on first
    // use) avoids the folder-lookup step's case sensitivity entirely; built once per reader rather
    // than per query since IArchiveReader.Files enumerates the whole archive.
    private readonly Dictionary<IArchiveReader, Dictionary<string, IArchiveFile>> _archiveIndexes = new();

    public ArchiveAwareFileProbe(string dataRoot, GameType gameType)
    {
        _looseProbe = new LooseFileProbe(dataRoot);

        var release = ToGameRelease(gameType);
        var archivePaths = Archive.GetApplicableArchivePaths(release, dataRoot);

        var readers = new List<IArchiveReader>();
        foreach (var archivePath in archivePaths)
        {
            readers.Add(Archive.CreateReader(release, archivePath));
        }
        _archiveReaders = readers;
    }

    public bool Exists(string relativeDataPath)
    {
        if (_looseProbe.Exists(relativeDataPath))
        {
            return true;
        }

        return TryFindArchiveFile(relativeDataPath, out _);
    }

    public Stream OpenRead(string relativeDataPath)
    {
        if (_looseProbe.Exists(relativeDataPath))
        {
            return _looseProbe.OpenRead(relativeDataPath);
        }

        if (TryFindArchiveFile(relativeDataPath, out var file))
        {
            return file!.AsStream();
        }

        throw new FileNotFoundException($"'{relativeDataPath}' was not found loose or in any applicable archive.");
    }

    /// <summary>Loose only - PBRNifPatcher-style json configs (the only current use of this method)
    /// are never packed into BSA/BA2 in practice, and indexing every archive's full file list just
    /// to search for json files would add real cost for no real-world benefit.</summary>
    public IEnumerable<string> EnumerateFiles(string relativeFolder, string extension) =>
        _looseProbe.EnumerateFiles(relativeFolder, extension);

    private bool TryFindArchiveFile(string relativeDataPath, out IArchiveFile? file)
    {
        foreach (var reader in _archiveReaders)
        {
            var index = GetOrBuildIndex(reader);
            if (index.TryGetValue(relativeDataPath, out var match))
            {
                file = match;
                return true;
            }
        }

        file = null;
        return false;
    }

    private Dictionary<string, IArchiveFile> GetOrBuildIndex(IArchiveReader reader)
    {
        if (_archiveIndexes.TryGetValue(reader, out var existing))
        {
            return existing;
        }

        var index = new Dictionary<string, IArchiveFile>(StringComparer.OrdinalIgnoreCase);
        foreach (var archiveFile in reader.Files)
        {
            // A handful of archives ship the same path under two different cases as distinct
            // entries - first one wins, matching how the game itself only ever sees one at a time.
            index.TryAdd(archiveFile.Path, archiveFile);
        }

        _archiveIndexes[reader] = index;
        return index;
    }

    private static GameRelease ToGameRelease(GameType gameType) => gameType switch
    {
        GameType.SkyrimSE => GameRelease.SkyrimSE,
        GameType.SkyrimLE => GameRelease.SkyrimLE,
        _ => throw new ArgumentOutOfRangeException(nameof(gameType), gameType, null),
    };

    public void Dispose()
    {
        foreach (var reader in _archiveReaders)
        {
            (reader as IDisposable)?.Dispose();
        }
    }
}
