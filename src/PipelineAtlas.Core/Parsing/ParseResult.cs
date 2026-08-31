using PipelineAtlas.Core.Model;

namespace PipelineAtlas.Core.Parsing;

// What parsing one pipeline/template file yields. Nodes are assembled by the
// analyzer; env/external references are materialized there so they're shared.
public sealed class ParseResult
{
    public List<Step> Steps { get; } = [];
    public List<Edge> Edges { get; } = [];
    public List<Flag> Flags { get; } = [];
    public List<Parameter> Parameters { get; } = [];
    public HashSet<string> EnvRefs { get; } = new(StringComparer.Ordinal);
    public HashSet<string> ExternalRefs { get; } = new(StringComparer.Ordinal);
    public List<string> FlowTrail { get; } = [];
}
