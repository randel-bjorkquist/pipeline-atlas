using System.Runtime.CompilerServices;
using PipelineAtlas.Core;
using PipelineAtlas.Core.Model;
using Xunit;

namespace PipelineAtlas.Core.Tests;

// When a target declares no matching cluster for a file, the engine groups it by
// top-level folder so the map still reads as subsystems with zero config.
public sealed class ClusteringTests
{
    private static Manifest Run() => Analyzer.Analyze(ConditionalDir);

    [Fact]
    public void GroupsUnclusteredFilesByTopLevelFolder()
    {
        Manifest m = Run(); // fixtures/conditional declares no clusters

        Node root = m.Nodes.First(n => n.Id == "pipeline:Pipeline");
        Node inner = m.Nodes.First(n => n.Id == "template:steps/inner");

        Assert.Equal("(root)", root.ClusterId); // a file at the target root
        Assert.Equal("steps", inner.ClusterId);  // steps/inner.yml -> "steps"
    }

    private static string ConditionalDir =>
        Path.GetFullPath(Path.Combine(TestDir(), "..", "..", "fixtures", "conditional"));

    private static string TestDir([CallerFilePath] string path = "") => Path.GetDirectoryName(path)!;
}
