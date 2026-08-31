namespace PipelineAtlas.Core;

// Injectable knobs for analyze(). The only non-deterministic inputs (clock, tool
// version) are here so callers/tests can pin them (CLAUDE.md sec 8.3). OnInfo
// carries best-effort diagnostics (work-item linking, malformed patterns) that
// must never be fatal; the CLI routes them to Information-level logging.
public sealed class AnalyzeOptions
{
    public DateTimeOffset? Now { get; init; }
    public string? ToolVersion { get; init; }
    public Action<string>? OnInfo { get; init; }
}
