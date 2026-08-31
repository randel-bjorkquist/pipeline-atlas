using System.Text.Json.Serialization;

namespace PipelineAtlas.Core.Model;

// The manifest vocabulary (CLAUDE.md sec 5). Enum members are PascalCase in C#
// and serialize to camelCase JSON via the shared serializer options; the one
// hyphenated value carries an explicit name.

public enum NodeType
{
    EntryPipeline,   // runnable pipeline (has a trigger / runs directly)
    Template,        // consumed via template: or extends
    EnvConfig,       // per-environment variables file
    PsModule,        // *.psm1
    PsScript,        // *.ps1
    Test,            // *.Tests.ps1
    Doc,             // *.md
    Data,            // *.json etc.
    AdoEnvironment,  // an ADO Environment (dev/qa/...) - inferred, not a file
    AdoResource,     // variable group, agent pool, approval/check - inferred
    External,        // referenced but outside the scanned folder, or a missing file
}

public enum NodeStatus
{
    Active,
    [JsonStringEnumMemberName("legacy-active")]
    LegacyActive,
    Dormant,
    Archived,
}

public enum NodeSource
{
    Parsed,
    Inferred, // ADO service node, or a missing/cross-repo reference; not a parsed file
}

public enum EdgeKind
{
    Extends,
    IncludesTemplate,
    CallsScript,
    RunsOnEnvironment,
    DeploysTo,
    ConsumesArtifact,
    ProducesArtifact,
    GatedBy,
    TestedBy,
    ReferencesExternal,
    Documents,
}

public enum StepKind
{
    Stage,
    Job,
    Step,
    TemplateInclude,
}

public enum TagKind
{
    Story,
    Feature,
    Bug,
}

public enum FlagSeverity
{
    Secret,
    Hardcode,
    Techdebt,
    Duplication,
    Antipattern,
}
