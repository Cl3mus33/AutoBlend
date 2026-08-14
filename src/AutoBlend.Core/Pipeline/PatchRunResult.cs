namespace AutoBlend.Core.Pipeline;

public sealed record PatchRunResult(
    int RecordsScanned,
    int MeshesPatchedInPlace,
    int MeshesDuplicated,
    int TextureSetsCreated,
    int AlternateTexturesAssigned,
    IReadOnlyList<string> Warnings,
    string? OutputEspPath);
