using System.Text.Json;
using PipelineAtlas.Core;
using PipelineAtlas.Core.Serialization;

namespace PipelineAtlas.Cli;

// `patlas analyze <folder> [-o manifest.json]` — scan a target and write its
// manifest. Work-item diagnostics from core arrive via OnInfo and are logged at
// Info (best-effort; never fatal). Refuses to analyze the Pipeline Atlas repo.
public static class AnalyzeCommand
{
    public static int Run(IReadOnlyList<string> argv)
    {
        ParsedArgs args = ArgParser.Parse(argv);
        if (args.Positionals.Count == 0)
        {
            throw new InvalidOperationException(
                "analyze needs a <folder>. Usage: patlas analyze <folder> [-o manifest.json]");
        }

        string targetDir = Path.GetFullPath(args.Positionals[0]);
        if (!Directory.Exists(targetDir))
        {
            throw new InvalidOperationException($"Not a folder: {targetDir}");
        }

        SelfGuard.AssertNotSelf(targetDir, args.Has("allow-self"));

        string output = Path.GetFullPath(args.Value("output") ?? "manifest.json");
        var manifest = Analyzer.Analyze(targetDir, new AnalyzeOptions { OnInfo = Log.Info });

        File.WriteAllText(output, JsonSerializer.Serialize(manifest, ManifestJson.Options) + "\n");

        int tagRefs = manifest.Nodes.Sum(n => n.Tags.Count);
        int linked = manifest.Nodes.Sum(n => n.Tags.Count(t => t.Url is not null));
        int flagCount = manifest.Nodes.Sum(n => n.Flags.Count);

        Log.Success(
            $"Wrote {Rel(output)} - {manifest.Nodes.Count} nodes, {manifest.Edges.Count} edges, " +
            $"{manifest.Steps.Count} steps, {manifest.Flows.Count} flows.");
        Log.Info($"Work items: {tagRefs} tag reference(s), {linked} linked.");
        if (flagCount > 0)
        {
            Log.Info($"{flagCount} flag(s) recorded (e.g. unresolved references, secrets, tech debt).");
        }

        return 0;
    }

    private static string Rel(string path)
    {
        string rel = Path.GetRelativePath(Directory.GetCurrentDirectory(), path);
        return rel.StartsWith("..", StringComparison.Ordinal) ? path : rel;
    }
}
