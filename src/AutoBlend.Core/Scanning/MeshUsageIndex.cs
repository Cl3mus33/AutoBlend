using Mutagen.Bethesda.Plugins;

namespace AutoBlend.Core.Scanning;

/// <summary>
/// One Static/MoveableStatic record's stake in a mesh: whether the blacklist (path or EditorID)
/// forces it to keep the original, unpatched mesh.
/// </summary>
public sealed record MeshUsage(FormKey RecordFormKey, bool IsBlacklisted);

/// <summary>
/// What to do with one mesh file once every record referencing it has been observed.
/// - No in-scope usage at all: leave it alone.
/// - In-scope usage only (no blacklisted record shares the file): patch in place, same filename —
///   every record referencing it benefits, none of them need a Model.File edit.
/// - Mixed usage: duplicate. The patched copy goes to a new path; only the in-scope records are
///   overridden to point at it. Blacklisted records are left completely untouched, still pointing
///   at the original mesh.
/// </summary>
public sealed record MeshPatchPlan(string MeshPath, bool PatchInPlace, IReadOnlyList<FormKey> DuplicateForRecords)
{
    public bool RequiresDuplication => DuplicateForRecords.Count > 0;
    public bool ShouldPatch => PatchInPlace || RequiresDuplication;
}

/// <summary>
/// Cross-references every Static/MoveableStatic record against the mesh it uses, so a mesh shared
/// by both an in-scope record and a blacklisted one gets duplicated instead of globally flipping
/// blend mode out from under the blacklisted record's intentional alpha testing.
/// </summary>
public sealed class MeshUsageIndex
{
    private readonly Dictionary<string, List<MeshUsage>> _usagesByMesh = new(StringComparer.OrdinalIgnoreCase);

    public void Record(string meshPath, FormKey recordFormKey, bool isBlacklisted)
    {
        if (!_usagesByMesh.TryGetValue(meshPath, out var list))
        {
            list = new List<MeshUsage>();
            _usagesByMesh[meshPath] = list;
        }

        list.Add(new MeshUsage(recordFormKey, isBlacklisted));
    }

    public MeshPatchPlan GetPlan(string meshPath)
    {
        if (!_usagesByMesh.TryGetValue(meshPath, out var usages) || usages.Count == 0)
        {
            return new MeshPatchPlan(meshPath, PatchInPlace: false, DuplicateForRecords: Array.Empty<FormKey>());
        }

        var hasInScope = usages.Any(u => !u.IsBlacklisted);
        if (!hasInScope)
        {
            return new MeshPatchPlan(meshPath, PatchInPlace: false, DuplicateForRecords: Array.Empty<FormKey>());
        }

        var hasBlacklisted = usages.Any(u => u.IsBlacklisted);
        if (!hasBlacklisted)
        {
            return new MeshPatchPlan(meshPath, PatchInPlace: true, DuplicateForRecords: Array.Empty<FormKey>());
        }

        var duplicateFor = usages.Where(u => !u.IsBlacklisted).Select(u => u.RecordFormKey).ToList();
        return new MeshPatchPlan(meshPath, PatchInPlace: false, DuplicateForRecords: duplicateFor);
    }

    /// <summary>
    /// <paramref name="variantIndex"/> distinguishes multiple duplicates of the same mesh (e.g. one
    /// mesh can need one duplicate per distinct set of records that share a physical alpha/diffuse
    /// treatment) - 0 keeps the original "_blend" suffix, higher indices get "_blend2", "_blend3", …
    /// </summary>
    public static string BuildDuplicateMeshPath(string originalMeshPath, int variantIndex = 0)
    {
        var directory = Path.GetDirectoryName(originalMeshPath) ?? string.Empty;
        var fileNameNoExt = Path.GetFileNameWithoutExtension(originalMeshPath);
        var extension = Path.GetExtension(originalMeshPath);
        var suffix = variantIndex <= 0 ? "_blend" : $"_blend{variantIndex + 1}";
        return Path.Combine(directory, $"{fileNameNoExt}{suffix}{extension}");
    }
}
