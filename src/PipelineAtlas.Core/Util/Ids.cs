using System.Text;
using PipelineAtlas.Core.Model;

namespace PipelineAtlas.Core.Util;

// Stable, deterministic id + slug helpers. File node ids are
// "<prefix>:<relpath-without-ext>" so they never collide across folders; inferred
// nodes carry their own scheme (env:<name>). Determinism here is what makes
// manifest.json diffs mean something (CLAUDE.md sec 8.3).
public static class Ids
{
    private static readonly IReadOnlyDictionary<NodeType, string> Prefix = new Dictionary<NodeType, string>
    {
        [NodeType.EntryPipeline] = "pipeline",
        [NodeType.Template] = "template",
        [NodeType.EnvConfig] = "envconfig",
        [NodeType.PsModule] = "psmodule",
        [NodeType.PsScript] = "psscript",
        [NodeType.Test] = "test",
        [NodeType.Doc] = "doc",
        [NodeType.Data] = "data",
        [NodeType.AdoEnvironment] = "env",
        [NodeType.AdoResource] = "res",
        [NodeType.External] = "external",
    };

    /// <summary>Drop the extension from a posix relative path.</summary>
    public static string StripExt(string relPath)
    {
        string posix = Globs.ToPosix(relPath);
        int slash = posix.LastIndexOf('/');
        int dot = posix.LastIndexOf('.');
        return dot > slash ? posix[..dot] : posix;
    }

    public static string FileNodeId(NodeType type, string relPath) =>
        $"{Prefix[type]}:{StripExt(relPath)}";

    public static string EnvNodeId(string name) => $"env:{Slug(name)}";

    public static string ExternalNodeId(string relPath) => $"external:{StripExt(relPath)}";

    /// <summary>Lowercase, punctuation-collapsed slug for names used inside ids.</summary>
    public static string Slug(string value)
    {
        var sb = new StringBuilder(value.Length);
        bool lastDash = false;
        foreach (char c in value.Trim().ToLowerInvariant())
        {
            if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9'))
            {
                sb.Append(c);
                lastDash = false;
            }
            else if (!lastDash)
            {
                sb.Append('-');
                lastDash = true;
            }
        }

        return sb.ToString().Trim('-');
    }
}
