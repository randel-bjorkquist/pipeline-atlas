using System.Runtime.CompilerServices;
using PipelineAtlas.Core;
using PipelineAtlas.Core.Model;
using Xunit;

namespace PipelineAtlas.Core.Tests;

// Detects steps that halt a run for human interaction — the Azure Pipelines
// ManualValidation task and the classic-release ManualIntervention task — so the
// viewer can mark them (🛑) alongside environment approval gates.
public sealed class ManualPauseTests
{
    private static Manifest Run() => Analyzer.Analyze(ManualPauseDir);

    [Fact]
    public void FlagsManualValidationAndInterventionStepsAsPauses()
    {
        Manifest m = Run();

        Assert.Equal(2, m.Steps.Count(s => s.ManualPause == true));
        Assert.Contains(m.Steps, s => s.ManualPause == true && s.Action!.StartsWith("ManualValidation", StringComparison.Ordinal));
        Assert.Contains(m.Steps, s => s.ManualPause == true && s.Action!.StartsWith("ManualIntervention", StringComparison.Ordinal));
    }

    [Fact]
    public void LeavesOrdinaryStepsUnflagged()
    {
        Manifest m = Run();

        Step deploy = m.Steps.First(s => s.Action == "script");
        Assert.Null(deploy.ManualPause); // omitted (not false) so the JSON stays clean
    }

    private static string ManualPauseDir =>
        Path.GetFullPath(Path.Combine(TestDir(), "..", "..", "fixtures", "manual-pause"));

    private static string TestDir([CallerFilePath] string path = "") => Path.GetDirectoryName(path)!;
}
