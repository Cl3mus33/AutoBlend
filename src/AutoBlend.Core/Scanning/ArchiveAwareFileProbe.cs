using System.Collections.Concurrent;
using AutoBlend.Core.Configuration;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Archives;
using Noggog;

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
    // than per query since IArchiveReader.Files enumerates the whole archive. ConcurrentDictionary
    // since this probe is shared across every mesh-processing thread once the main per-mesh loop
    // runs in parallel (see PatchOrchestrator) - Exists/OpenRead are called directly from that hot
    // path, not behind any other synchronization.
    private readonly ConcurrentDictionary<IArchiveReader, Dictionary<string, IArchiveFile>> _archiveIndexes = new();

    public ArchiveAwareFileProbe(string dataRoot, GameType gameType)
    {
        _looseProbe = new LooseFileProbe(dataRoot);

        var release = ToGameRelease(gameType);
        var archivePaths = GetApplicableArchivePathsSafe(release, dataRoot);

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

    private Dictionary<string, IArchiveFile> GetOrBuildIndex(IArchiveReader reader) =>
        _archiveIndexes.GetOrAdd(reader, BuildIndex);

    private static Dictionary<string, IArchiveFile> BuildIndex(IArchiveReader reader)
    {
        var index = new Dictionary<string, IArchiveFile>(StringComparer.OrdinalIgnoreCase);
        foreach (var archiveFile in reader.Files)
        {
            // A handful of archives ship the same path under two different cases as distinct
            // entries - first one wins, matching how the game itself only ever sees one at a time.
            index.TryAdd(archiveFile.Path, archiveFile);
        }

        return index;
    }

    /// <summary>
    /// Mutagen's own Archive.GetApplicableArchivePaths sorts every matching archive by a priority
    /// comparer that can throw NotImplementedException from deep inside Mutagen itself - reported
    /// directly and confirmed against Mutagen's own source (Archives/DI/IArchiveListingDetailsProvider.cs,
    /// version 0.54.4): two archives whose names collapse to the same base+suffix pair after
    /// stripping a " - Suffix" segment reach a branch Mutagen never implemented. Real-world trigger:
    /// Creation Club content archives, whose names commonly collide this way. We don't actually need
    /// Mutagen's own priority ordering here - TryFindArchiveFile only cares about which archives
    /// exist at all (first match wins in whatever order they're returned), so falling back to a
    /// plain, unsorted directory listing on failure keeps the whole run from crashing over a
    /// dependency bug that has nothing to do with which files are actually being looked up.
    /// </summary>
    private static IEnumerable<FilePath> GetApplicableArchivePathsSafe(GameRelease release, string dataRoot)
    {
        try
        {
            return Archive.GetApplicableArchivePaths(release, dataRoot).ToList();
        }
        catch (Exception)
        {
            var extension = Archive.GetExtension(release);
            return Directory.Exists(dataRoot)
                ? Directory.EnumerateFiles(dataRoot, "*" + extension).Select(path => (FilePath)path).ToList()
                : Enumerable.Empty<FilePath>();
        }
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
