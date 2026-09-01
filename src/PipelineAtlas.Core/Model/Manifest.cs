namespace PipelineAtlas.Core.Model;

// The manifest contract — the single shape everything downstream renders from,
// mirrored by Schema/manifest.schema.json (validated on every build). Records are
// immutable; optional (nullable) members are omitted from JSON when null.

public sealed record Tag
{
    public required TagKind Kind { get; init; }
    public required string Id { get; init; }
    public string? Url { get; init; }
}

public sealed record Flag
{
    public required FlagSeverity Severity { get; init; }
    public required string Note { get; init; }
    public string? StepId { get; init; }
}

public sealed record Parameter
{
    public required string Name { get; init; }
    public string? Type { get; init; }
    public string? Default { get; init; }
    public string? Description { get; init; } // seeded from the comment above the parameter
}

public sealed record Node
{
    public required string Id { get; init; }          // stable slug, e.g. "pipeline:DevCI"
    public required NodeType Type { get; init; }
    public string? Path { get; init; }                // target-relative path for file nodes
    public required string Title { get; init; }
    public string? Label { get; init; }               // readable display name (T2b), e.g. "Deploy to DEV (DevCI)"
    public required string Purpose { get; init; }     // plain-language; seeded from header comment
    public string? ClusterId { get; init; }           // from .patlas.json
    public string? Category { get; init; }            // "deployment" | "policy" | ... (T2a)
    public string? Trigger { get; init; }             // "CI: <branch>" | "manual" | "schedule" | "PR gate"
    public string? Pool { get; init; }
    public string? Body { get; init; }                 // markdown content, for doc (.md) nodes (T4b)
    public IReadOnlyList<Parameter>? Parameters { get; init; } // pipeline/template inputs
    public IReadOnlyList<Tag> Tags { get; init; } = [];
    public IReadOnlyList<Flag> Flags { get; init; } = [];
    public required NodeStatus Status { get; init; }
    public NodeSource? Source { get; init; }           // inferred = ADO/missing, not a parsed file
}

public sealed record Edge
{
    public required string Id { get; init; }
    public required string From { get; init; }
    public required string To { get; init; }
    public required EdgeKind Kind { get; init; }
    public string? AtStepId { get; init; }
    public IReadOnlyList<Tag>? Tags { get; init; }
}

public sealed record Step
{
    public required string Id { get; init; }           // e.g. "DevCI/Deploy_Dev/apply-config"
    public required string NodeId { get; init; }
    public string? ParentId { get; init; }             // stage -> job -> step nesting
    public required StepKind Kind { get; init; }
    public required string Name { get; init; }
    public required string Doc { get; init; }          // seeded from inline comments
    public string? Action { get; init; }               // task/command summary
    public IReadOnlyList<string>? ExternalDeps { get; init; }
    public bool? ManualPause { get; init; }             // true = pauses the run for human interaction (ManualValidation/ManualIntervention)
    public IReadOnlyList<Tag> Tags { get; init; } = [];
}

public sealed record Flow
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required string Trigger { get; init; }
    public required IReadOnlyList<string> Path { get; init; } // ordered node/step ids traversed
    public string? Notes { get; init; }
}

public sealed record TargetInfo
{
    public required string DisplayName { get; init; }
    public string? Description { get; init; }
    public required string GeneratedAt { get; init; }
    public required string ToolVersion { get; init; }
}

public sealed record Manifest
{
    public required TargetInfo Target { get; init; }
    public required IReadOnlyList<Node> Nodes { get; init; }
    public required IReadOnlyList<Edge> Edges { get; init; }
    public required IReadOnlyList<Step> Steps { get; init; }
    public required IReadOnlyList<Flow> Flows { get; init; }
}
