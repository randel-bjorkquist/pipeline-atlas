using PipelineAtlas.Core.Configuration;

namespace PipelineAtlas.Cli;

// `patlas init <folder>` — drop a starter .patlas.json (CLAUDE.md sec 4) so a new
// target is one edit away. By default it's written into the target folder; pass
// --config <path> to write it elsewhere (keeping a read-only/source-controlled
// target untouched). Refuses to clobber an existing config unless --force.
public static class InitCommand
{
    public static int Run(IReadOnlyList<string> argv)
    {
        ParsedArgs args = ArgParser.Parse(argv);
        if (args.Positionals.Count == 0)
        {
            throw new InvalidOperationException(
                "init needs a <folder>. Usage: patlas init <folder> [--config <path>]");
        }

        string folder = args.Positionals[0];
        string targetDir = Path.GetFullPath(folder);

        // --config writes the starter to an external path and leaves the target
        // alone; otherwise it goes into the target folder (created if needed).
        string? configOut = args.Value("config");
        string configPath;
        if (configOut is not null)
        {
            configPath = Path.GetFullPath(configOut);
            string? parent = Path.GetDirectoryName(configPath);
            if (!string.IsNullOrEmpty(parent))
            {
                Directory.CreateDirectory(parent);
            }
        }
        else
        {
            Directory.CreateDirectory(targetDir);
            configPath = Path.Combine(targetDir, ConfigLoader.ConfigFilename);
        }

        if (File.Exists(configPath) && !args.Has("force"))
        {
            throw new InvalidOperationException($"{configPath} already exists. Pass --force to overwrite.");
        }

        File.WriteAllText(configPath, Starter(Path.GetFileName(targetDir.TrimEnd('/', '\\'))));

        Log.Success($"Wrote {Rel(configPath)}.");
        string next = configOut is not null
            ? $"patlas view {Quote(folder)} --config {Quote(configOut)}"
            : $"patlas view {Quote(folder)}";
        Log.Info($"Next: set displayName, workItems.baseUrl and clusters, then run `{next}`.");
        return 0;
    }

    private static string Quote(string path) =>
        path.Contains(' ', StringComparison.Ordinal) ? $"\"{path}\"" : path;

    private static string Starter(string name)
    {
        const string template = """
        {
          "displayName": "__NAME__",
          "description": "One line about this target.",
          "scan": {
            "include": ["**/*.yml", "**/*.yaml", "**/*.psm1", "**/*.ps1", "**/*.md", "**/*.json"],
            "exclude": ["**/node_modules/**", "**/.git/**"]
          },
          "workItems": {
            "baseUrl": "https://dev.azure.com/YOUR_ORG/YOUR_PROJECT/_workitems/edit/",
            "tagPatterns": {
              "story": "story\\s+(\\d+)",
              "feature": "feature\\s+(\\d+)",
              "bug": "bug\\s+(\\d+)"
            }
          },
          "clusters": [
            { "id": "example", "label": "Example subsystem", "match": ["**/foo.yml", "**/Bar.psm1"] }
          ],
          "nodeStatus": [
            { "match": ["**/.archive/**"], "status": "archived" }
          ],
          "archive": { "match": ["**/.archive/**"], "inventoryOnly": true }
        }

        """;
        return template.Replace("__NAME__", name);
    }

    private static string Rel(string path)
    {
        string rel = Path.GetRelativePath(Directory.GetCurrentDirectory(), path);
        return rel.StartsWith("..", StringComparison.Ordinal) ? path : rel;
    }
}
