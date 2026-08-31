using PipelineAtlas.Core.Configuration;

namespace PipelineAtlas.Cli;

// `patlas init <folder>` — drop a starter .patlas.json (CLAUDE.md sec 4) so a new
// target is one edit away. Creates the folder if needed; refuses to clobber an
// existing config unless --force.
public static class InitCommand
{
    public static int Run(IReadOnlyList<string> argv)
    {
        ParsedArgs args = ArgParser.Parse(argv);
        if (args.Positionals.Count == 0)
        {
            throw new InvalidOperationException("init needs a <folder>. Usage: patlas init <folder>");
        }

        string folder = args.Positionals[0];
        string targetDir = Path.GetFullPath(folder);
        Directory.CreateDirectory(targetDir);

        string configPath = Path.Combine(targetDir, ConfigLoader.ConfigFilename);
        if (File.Exists(configPath) && !args.Has("force"))
        {
            throw new InvalidOperationException($"{configPath} already exists. Pass --force to overwrite.");
        }

        File.WriteAllText(configPath, Starter(Path.GetFileName(targetDir.TrimEnd('/', '\\'))));

        Log.Success($"Wrote {Rel(configPath)}.");
        Log.Info($"Next: set displayName, workItems.baseUrl and clusters, then run `patlas analyze {folder}`.");
        return 0;
    }

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
