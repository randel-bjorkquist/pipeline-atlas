using System.Text.Json;
using System.Text.Json.Serialization;

namespace PipelineAtlas.Core.Configuration;

// Loads and normalizes a target's .patlas.json (CLAUDE.md sec 4). Everything the
// engine needs to stay project-agnostic comes from here; paths/globs are relative
// to the target folder.

public sealed record ScanConfig(IReadOnlyList<string> Include, IReadOnlyList<string> Exclude);

public sealed record WorkItemsConfig(string? BaseUrl, IReadOnlyDictionary<string, string> TagPatterns);

public sealed record ClusterConfig(string Id, string Label, IReadOnlyList<string> Match);

public sealed record NodeStatusRule(IReadOnlyList<string> Match, string Status);

public sealed record ArchiveConfig(IReadOnlyList<string> Match, bool InventoryOnly);

// T2a: assign a category ("deployment" | "policy" | ...) to matched nodes.
public sealed record CategoryRule(IReadOnlyList<string> Match, string Category);

// T2b: give matched nodes a readable display label.
public sealed record LabelRule(IReadOnlyList<string> Match, string Label);

// Approval gates can't be seen in files (they live in Azure DevOps); the target
// declares which environments require recorded approval (per DEPLOY-GATE.md etc.).
public sealed record GateRule(string Environment, string? Note);

public sealed record PatlasConfig(
    string DisplayName,
    string? Description,
    ScanConfig Scan,
    WorkItemsConfig WorkItems,
    IReadOnlyList<ClusterConfig> Clusters,
    IReadOnlyList<NodeStatusRule> NodeStatus,
    ArchiveConfig? Archive,
    IReadOnlyList<CategoryRule> Categories,
    IReadOnlyList<LabelRule> Labels,
    IReadOnlyList<GateRule> Gates);

public static class ConfigLoader
{
    public const string ConfigFilename = ".patlas.json";

    public static readonly string[] DefaultInclude =
        ["**/*.yml", "**/*.yaml", "**/*.psm1", "**/*.ps1", "**/*.md", "**/*.json"];

    public static readonly string[] DefaultExclude = ["**/node_modules/**", "**/.git/**"];

    public static readonly IReadOnlyDictionary<string, string> DefaultTagPatterns =
        new Dictionary<string, string>
        {
            ["story"] = @"story\s+(\d+)",
            ["feature"] = @"feature\s+(\d+)",
            ["bug"] = @"bug\s+(\d+)",
        };

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>Read .patlas.json from a target folder and fill in defaults.</summary>
    public static PatlasConfig Load(string targetDir)
    {
        string configPath = Path.Combine(targetDir, ConfigFilename);
        if (!File.Exists(configPath))
        {
            throw new FileNotFoundException(
                $"No {ConfigFilename} found in target folder: {targetDir}. " +
                "Run `patlas init <folder>` to create one.",
                configPath);
        }

        Dto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<Dto>(File.ReadAllText(configPath), ReadOptions);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"{configPath} is not valid JSON: {ex.Message}", ex);
        }

        if (dto is null || string.IsNullOrWhiteSpace(dto.DisplayName))
        {
            throw new InvalidOperationException($"{configPath} is missing required \"displayName\".");
        }

        return new PatlasConfig(
            DisplayName: dto.DisplayName,
            Description: dto.Description,
            Scan: new ScanConfig(
                (IReadOnlyList<string>?)dto.Scan?.Include ?? DefaultInclude,
                (IReadOnlyList<string>?)dto.Scan?.Exclude ?? DefaultExclude),
            WorkItems: new WorkItemsConfig(
                dto.WorkItems?.BaseUrl,
                dto.WorkItems?.TagPatterns ?? DefaultTagPatterns),
            Clusters: dto.Clusters?.Select(c =>
                new ClusterConfig(c.Id ?? "", c.Label ?? "", c.Match ?? [])).ToArray() ?? [],
            NodeStatus: dto.NodeStatus?.Select(r =>
                new NodeStatusRule(r.Match ?? [], r.Status ?? "active")).ToArray() ?? [],
            Archive: dto.Archive is null
                ? null
                : new ArchiveConfig(dto.Archive.Match ?? [], dto.Archive.InventoryOnly ?? false),
            Categories: dto.Categories?.Select(c =>
                new CategoryRule(c.Match ?? [], c.Category ?? "")).Where(c => c.Category.Length > 0).ToArray() ?? [],
            Labels: dto.Labels?.Select(l =>
                new LabelRule(l.Match ?? [], l.Label ?? "")).Where(l => l.Label.Length > 0).ToArray() ?? [],
            Gates: dto.Gates?.Select(g =>
                new GateRule(g.Environment ?? "", g.Note)).Where(g => g.Environment.Length > 0).ToArray() ?? []);
    }

    // Nullable mirror of the file for tolerant deserialization.
    private sealed class Dto
    {
        public string? DisplayName { get; set; }
        public string? Description { get; set; }
        public ScanDto? Scan { get; set; }
        public WorkItemsDto? WorkItems { get; set; }
        public List<ClusterDto>? Clusters { get; set; }
        public List<NodeStatusDto>? NodeStatus { get; set; }
        public ArchiveDto? Archive { get; set; }
        public List<CategoryDto>? Categories { get; set; }
        public List<LabelDto>? Labels { get; set; }
        public List<GateDto>? Gates { get; set; }
    }

    private sealed class CategoryDto
    {
        public List<string>? Match { get; set; }
        public string? Category { get; set; }
    }

    private sealed class LabelDto
    {
        public List<string>? Match { get; set; }
        public string? Label { get; set; }
    }

    private sealed class GateDto
    {
        public string? Environment { get; set; }
        public string? Note { get; set; }
    }

    private sealed class ScanDto
    {
        public List<string>? Include { get; set; }
        public List<string>? Exclude { get; set; }
    }

    private sealed class WorkItemsDto
    {
        public string? BaseUrl { get; set; }
        public Dictionary<string, string>? TagPatterns { get; set; }
    }

    private sealed class ClusterDto
    {
        public string? Id { get; set; }
        public string? Label { get; set; }
        public List<string>? Match { get; set; }
    }

    private sealed class NodeStatusDto
    {
        public List<string>? Match { get; set; }
        public string? Status { get; set; }
    }

    private sealed class ArchiveDto
    {
        public List<string>? Match { get; set; }
        public bool? InventoryOnly { get; set; }
    }
}
