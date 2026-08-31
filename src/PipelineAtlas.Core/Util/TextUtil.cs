using System.Text;
using System.Text.RegularExpressions;
using PipelineAtlas.Core.Model;

namespace PipelineAtlas.Core.Util;

// Prose + work-item extraction shared by every file node. Docs are seeded from a
// file's header comment (CLAUDE.md sec 8.6); tags are harvested from comments via
// the target's workItems.tagPatterns (sec 6).
public static class TextUtil
{
    private static readonly HashSet<string> KnownTagKinds = new(StringComparer.Ordinal)
    {
        "story", "feature", "bug",
    };

    // Placeholders the starter .patlas.json ships with. Work-item linking is
    // best-effort: an unfilled baseUrl must not produce broken links, so these are
    // treated as "not set" and tags parse without URLs.
    private static readonly string[] PlaceholderMarkers = ["your_org", "your_project", "placeholder"];

    private static readonly Regex HeaderLine = new(@"^\s*#\s?(.*)$", RegexOptions.Compiled);

    /// <summary>Returns the baseUrl only if it is usable (non-empty and not a placeholder).</summary>
    public static string? UsableBaseUrl(string? baseUrl)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            return null;
        }

        string lower = baseUrl.ToLowerInvariant();
        return PlaceholderMarkers.Any(lower.Contains) ? null : baseUrl;
    }

    /// <summary>
    /// Leading contiguous block of `#` comment lines. Line/paragraph structure is
    /// preserved (the viewer renders it pre-wrap) so long headers stay readable.
    /// </summary>
    public static string HeaderComment(string raw)
    {
        var parts = new List<string>();
        foreach (string line in raw.Split('\n'))
        {
            string trimmed = line.TrimEnd('\r');
            Match m = HeaderLine.Match(trimmed);
            if (!m.Success)
            {
                break;
            }

            parts.Add(m.Groups[1].Value.TrimEnd());
        }

        return string.Join("\n", parts).Trim('\n', ' ');
    }

    /// <summary>Harvest work-item tags from text. Deterministic: deduped, sorted by kind then id.</summary>
    public static IReadOnlyList<Tag> HarvestTags(
        string text,
        IReadOnlyDictionary<string, string> tagPatterns,
        string? baseUrl)
    {
        var seen = new Dictionary<string, Tag>(StringComparer.Ordinal);
        foreach ((string kind, string pattern) in tagPatterns)
        {
            if (!KnownTagKinds.Contains(kind) || !TryParseKind(kind, out TagKind tagKind))
            {
                continue;
            }

            Regex re;
            try
            {
                re = new Regex(pattern, RegexOptions.IgnoreCase);
            }
            catch (ArgumentException)
            {
                continue; // best-effort: a malformed pattern is skipped, never fatal
            }

            foreach (Match match in re.Matches(text))
            {
                if (match.Groups.Count < 2)
                {
                    continue;
                }

                string id = match.Groups[1].Value;
                string key = $"{kind}:{id}";
                if (seen.ContainsKey(key))
                {
                    continue;
                }

                seen[key] = new Tag
                {
                    Kind = tagKind,
                    Id = id,
                    Url = baseUrl is null ? null : baseUrl + id,
                };
            }
        }

        return seen.Values
            .OrderBy(t => t.Kind.ToString(), StringComparer.Ordinal)
            .ThenBy(t => long.TryParse(t.Id, out long n) ? n : long.MaxValue)
            .ThenBy(t => t.Id, StringComparer.Ordinal)
            .ToArray();
    }

    private static bool TryParseKind(string kind, out TagKind result)
    {
        switch (kind)
        {
            case "story": result = TagKind.Story; return true;
            case "feature": result = TagKind.Feature; return true;
            case "bug": result = TagKind.Bug; return true;
            default: result = default; return false;
        }
    }

}
