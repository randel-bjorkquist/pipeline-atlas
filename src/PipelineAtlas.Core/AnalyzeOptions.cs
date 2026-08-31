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

    // Optional explicit path to the target's .patlas.json. When null, the config
    // is read from <targetDir>/.patlas.json. Set this to keep the config outside
    // the target (which stays a read-only input, CLAUDE.md sec 8.1) — e.g. a
    // git-ignored config in the Pipeline Atlas checkout describing a source-
    // controlled target folder. Paths/globs inside the config remain relative to
    // the target folder regardless of where the file lives.
    public string? ConfigPath { get; init; }
}
