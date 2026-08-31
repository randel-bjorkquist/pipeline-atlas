using Microsoft.Extensions.FileSystemGlobbing;

namespace PipelineAtlas.Core.Util;

// Glob matching for cluster / status / archive rules. Target-relative posix paths
// in; first-matching rule wins (documented semantics: rules are evaluated in the
// order they appear in .patlas.json).
public static class Globs
{
    public static string ToPosix(string path) => path.Replace('\\', '/');

    /// <summary>True if any glob matches the (posix) relative path.</summary>
    public static bool Matches(IReadOnlyList<string> globs, string relPath)
    {
        if (globs.Count == 0)
        {
            return false;
        }

        var matcher = new Matcher(StringComparison.OrdinalIgnoreCase);
        matcher.AddIncludePatterns(globs);
        return matcher.Match(ToPosix(relPath)).HasMatches;
    }

    /// <summary>First rule whose globs match wins; returns the matching rule or default.</summary>
    public static T? FirstMatch<T>(IReadOnlyList<T> rules, Func<T, IReadOnlyList<string>> selector, string relPath)
        where T : class
    {
        foreach (T rule in rules)
        {
            if (Matches(selector(rule), relPath))
            {
                return rule;
            }
        }

        return null;
    }
}
