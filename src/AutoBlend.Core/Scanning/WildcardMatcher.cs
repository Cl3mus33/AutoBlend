using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace AutoBlend.Core.Scanning;

/// <summary>
/// Matches paths against simple "*" wildcard patterns (e.g. "*\glass\*"), case-insensitive,
/// treating "/" and "\" as equivalent — matching the AutoSeasons blocklist convention.
/// </summary>
public static class WildcardMatcher
{
    // Every pattern passed through here comes from one of a handful of small, fixed
    // PatcherSettings lists (mesh blacklist, EditorID keywords, AutoGenerateAllowlist) that never
    // change during a run — but MatchesAny/IsMatch is called once per record/shape scanned, which
    // on a real modlist is tens of thousands of calls. RegexOptions.Compiled does real
    // IL-generation/JIT work per construction, so building a fresh Regex on every call (as before)
    // meant redoing that work for the exact same pattern string over and over. Caching the compiled
    // Regex per normalized pattern means each unique pattern is only ever compiled once per
    // process; behavior is identical since BuildRegex is a pure function of the normalized pattern.
    private static readonly ConcurrentDictionary<string, Regex> RegexCache = new();

    public static bool IsMatch(string path, string pattern)
    {
        var normalizedPath = Normalize(path);
        var regex = GetOrBuildRegex(pattern);
        return regex.IsMatch(normalizedPath);
    }

    public static bool MatchesAny(string path, IEnumerable<string> patterns)
    {
        var normalizedPath = Normalize(path);
        foreach (var pattern in patterns)
        {
            if (GetOrBuildRegex(pattern).IsMatch(normalizedPath))
            {
                return true;
            }
        }
        return false;
    }

    private static string Normalize(string value) => value.Replace('\\', '/').Trim();

    private static Regex GetOrBuildRegex(string pattern)
    {
        var normalizedPattern = Normalize(pattern);
        return RegexCache.GetOrAdd(normalizedPattern, static p =>
        {
            var escaped = Regex.Escape(p).Replace(@"\*", ".*");
            return new Regex($"^{escaped}$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        });
    }
}
