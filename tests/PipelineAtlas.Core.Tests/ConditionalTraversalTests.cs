using System.Runtime.CompilerServices;
using PipelineAtlas.Core;
using PipelineAtlas.Core.Model;
using Xunit;

namespace PipelineAtlas.Core.Tests;

// Regression guard for template includes nested inside ${{ if/else }} expression
// blocks. Before the walker descended into these, real Azure Pipelines templates
// lost ~three-quarters of their includes (pha-web: 15 captured of 62).
public sealed class ConditionalTraversalTests
{
    private static Manifest Run() => Analyzer.Analyze(ConditionalDir);

    [Fact]
    public void CapturesIncludesNestedInIfAndElseBlocks()
    {
        Edge[] includes = [.. Run().Edges.Where(e => e.Kind == EdgeKind.IncludesTemplate)];
        Assert.Contains(includes, e => e is { From: "pipeline:Pipeline", To: "template:steps/inner" });
        Assert.Contains(includes, e => e is { From: "pipeline:Pipeline", To: "template:steps/fallback" });
    }

    [Fact]
    public void DoesNotInventExternalNodesForResolvedNestedIncludes()
    {
        Assert.DoesNotContain(Run().Nodes, n => n.Type == NodeType.External);
    }

    private static string ConditionalDir =>
        Path.GetFullPath(Path.Combine(TestDir(), "..", "..", "fixtures", "conditional"));

    private static string TestDir([CallerFilePath] string path = "") => Path.GetDirectoryName(path)!;
}
