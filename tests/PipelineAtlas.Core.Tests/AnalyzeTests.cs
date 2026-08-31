using System.Runtime.CompilerServices;
using System.Text.Json;
using PipelineAtlas.Core;
using PipelineAtlas.Core.Building;
using PipelineAtlas.Core.Model;
using PipelineAtlas.Core.Serialization;
using Xunit;

namespace PipelineAtlas.Core.Tests;

// Vertical-slice test for the engine. Proves the sample fixture parses into the
// expected nodes/edges/steps/flows, that the unresolved template reference becomes
// an external node + techdebt flag, and that output is deterministic via a golden
// file. Regenerate the golden with: UPDATE_GOLDEN=1 dotnet test
public sealed class AnalyzeTests
{
    // Pinned clock + version so the manifest is byte-stable.
    private static readonly AnalyzeOptions Fixed = new()
    {
        Now = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero),
        ToolVersion = "0.0.0-test",
    };

    private static Manifest Run() => Analyzer.Analyze(SampleDir, Fixed);

    [Fact]
    public void PassesSchemaValidation()
    {
        IReadOnlyList<string> errors = ManifestValidator.Validate(Run());
        Assert.Empty(errors);
    }

    [Fact]
    public void ResolvesBuildAndDeployTemplateIncludes()
    {
        Edge[] includes = [.. Run().Edges.Where(e => e.Kind == EdgeKind.IncludesTemplate)];
        Assert.Contains(includes, e => e is { From: "pipeline:Entry", To: "template:templates/build" });
        Assert.Contains(includes, e => e is { From: "pipeline:Entry", To: "template:templates/deploy" });
    }

    [Fact]
    public void EmitsExternalNodeAndTechdebtFlagForUnresolvedReference()
    {
        Manifest m = Run();
        Node? ext = m.Nodes.FirstOrDefault(n => n.Id == "external:templates/steps-version");
        Assert.NotNull(ext);
        Assert.Equal(NodeType.External, ext!.Type);
        Assert.Equal(NodeSource.Inferred, ext.Source);

        Node build = m.Nodes.First(n => n.Id == "template:templates/build");
        Flag? flag = build.Flags.FirstOrDefault(f => f.Severity == FlagSeverity.Techdebt);
        Assert.NotNull(flag);
        Assert.Contains("steps-version.yml", flag!.Note);

        Assert.Contains(m.Edges, e =>
            e.To == "external:templates/steps-version" && e.Kind == EdgeKind.IncludesTemplate);
    }

    [Fact]
    public void InfersDevEnvironmentWithDeploysToAndRunsOnEnvironment()
    {
        Manifest m = Run();
        Node? env = m.Nodes.FirstOrDefault(n => n.Id == "env:dev");
        Assert.NotNull(env);
        Assert.Equal(NodeType.AdoEnvironment, env!.Type);
        Assert.Equal(NodeSource.Inferred, env.Source);

        Assert.Contains(m.Edges, e =>
            e.Kind == EdgeKind.DeploysTo && e is { From: "pipeline:Entry", To: "env:dev" });
        Assert.Contains(m.Edges, e =>
            e.Kind == EdgeKind.RunsOnEnvironment && e is { From: "template:templates/deploy", To: "env:dev" });
    }

    [Fact]
    public void HarvestsWorkItemTagsFromComments()
    {
        Node entry = Run().Nodes.First(n => n.Id == "pipeline:Entry");
        Assert.Contains(entry.Tags, t =>
            t is { Kind: TagKind.Feature, Id: "100", Url: "https://dev.azure.com/sample-org/sample-project/_workitems/edit/100" });
        Assert.Contains(entry.Tags, t => t is { Kind: TagKind.Story, Id: "101" });
    }

    [Fact]
    public void DerivesOneFlowPerEntryPipeline()
    {
        Manifest m = Run();
        Flow flow = Assert.Single(m.Flows);
        Assert.Equal("CI: main", flow.Trigger);
        Assert.Equal(
            new[]
            {
                "pipeline:Entry",
                "Entry/Build",
                "template:templates/build",
                "Entry/Deploy_Dev",
                "template:templates/deploy",
                "env:dev",
            },
            flow.Path);
    }

    [Fact]
    public void ExternalConfigPathProducesTheSameManifest()
    {
        // Copy the sample's files (WITHOUT its .patlas.json) into a temp target, and
        // point --config/ConfigPath at the config elsewhere — the target stays free
        // of any .patlas.json yet analysis matches the in-folder case byte-for-byte.
        string tempTarget = Path.Combine(Path.GetTempPath(), "patlas-extcfg-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempTarget);
        try
        {
            foreach (string src in Directory.EnumerateFiles(SampleDir, "*", SearchOption.AllDirectories))
            {
                if (Path.GetFileName(src) == ".patlas.json")
                {
                    continue;
                }

                string rel = Path.GetRelativePath(SampleDir, src);
                string dest = Path.Combine(tempTarget, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                File.Copy(src, dest, overwrite: true);
            }

            Assert.False(File.Exists(Path.Combine(tempTarget, ".patlas.json")));

            var opts = new AnalyzeOptions
            {
                Now = Fixed.Now,
                ToolVersion = Fixed.ToolVersion,
                ConfigPath = Path.Combine(SampleDir, ".patlas.json"),
            };

            string external = JsonSerializer.Serialize(Analyzer.Analyze(tempTarget, opts), ManifestJson.Options);
            string inFolder = JsonSerializer.Serialize(Run(), ManifestJson.Options);
            Assert.Equal(inFolder, external);
        }
        finally
        {
            Directory.Delete(tempTarget, recursive: true);
        }
    }

    [Fact]
    public void MatchesGoldenManifest()
    {
        string serialized = JsonSerializer.Serialize(Run(), ManifestJson.Options) + "\n";
        if (Environment.GetEnvironmentVariable("UPDATE_GOLDEN") is not null || !File.Exists(GoldenPath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(GoldenPath)!);
            File.WriteAllText(GoldenPath, serialized);
        }

        Assert.Equal(File.ReadAllText(GoldenPath), serialized);
    }

    private static string SampleDir =>
        Path.GetFullPath(Path.Combine(TestDir(), "..", "..", "fixtures", "sample"));

    private static string GoldenPath =>
        Path.Combine(TestDir(), "Fixtures", "sample.manifest.json");

    private static string TestDir([CallerFilePath] string path = "") => Path.GetDirectoryName(path)!;
}
