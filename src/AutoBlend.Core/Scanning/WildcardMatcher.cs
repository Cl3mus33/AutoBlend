using System.Text.RegularExpressions;

namespace AutoBlend.Core.Scanning;

/// <summary>
/// Matches paths against simple "*" wildcard patterns (e.g. "*\glass\*"), case-insensitive,
/// treating "/" and "\" as equivalent — matching the AutoSeasons blocklist convention.
/// </summary>
public static class WildcardMatcher
{
    public static bool IsMatch(string path, string pattern)
    {
        var normalizedPath = Normalize(path);
        var regex = BuildRegex(Normalize(pattern));
        return regex.IsMatch(normalizedPath);
    }

    public static bool MatchesAny(string path, IEnumerable<string> patterns)
    {
        var normalizedPath = Normalize(path);
        foreach (var pattern in patterns)
        {
            if (BuildRegex(Normalize(pattern)).IsMatch(normalizedPath))
            {
                return true;
            }
        }
        return false;
    }

    private static string Normalize(string value) => value.Replace('\\', '/').Trim();

    private static Regex BuildRegex(string pattern)
    {
        var escaped = Regex.Escape(pattern).Replace(@"\*", ".*");
        return new Regex($"^{escaped}$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    }
}
