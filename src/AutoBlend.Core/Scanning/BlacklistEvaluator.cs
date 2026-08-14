using AutoBlend.Core.Configuration;

namespace AutoBlend.Core.Scanning;

/// <summary>
/// Applies the two exclusion rules from <see cref="PatcherSettings"/>: mesh path wildcards and
/// EditorID substring keywords. Either match is enough to skip a candidate.
/// </summary>
public sealed class BlacklistEvaluator
{
    private readonly IReadOnlyList<string> _meshPatterns;
    private readonly IReadOnlyList<string> _editorIdKeywords;

    public BlacklistEvaluator(PatcherSettings settings)
    {
        _meshPatterns = settings.MeshBlacklist;
        _editorIdKeywords = settings.EditorIdBlacklistKeywords;
    }

    public bool IsMeshBlacklisted(string meshPath) => WildcardMatcher.MatchesAny(meshPath, _meshPatterns);

    public bool IsEditorIdBlacklisted(string editorId)
    {
        foreach (var keyword in _editorIdKeywords)
        {
            if (editorId.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }
}
