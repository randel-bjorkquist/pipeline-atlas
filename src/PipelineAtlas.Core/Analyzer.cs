using System.Globalization;
using System.Text.RegularExpressions;
using YamlDotNet.RepresentationModel;
using PipelineAtlas.Core.Building;
using PipelineAtlas.Core.Configuration;
using PipelineAtlas.Core.Model;
using PipelineAtlas.Core.Parsing;
using PipelineAtlas.Core.Scanning;
using PipelineAtlas.Core.Util;

namespace PipelineAtlas.Core;

// Public API: Analyze(folder) -> validated Manifest. Deterministic given the same
// folder + .patlas.json; the only non-deterministic inputs (clock, tool version)
// are injectable via AnalyzeOptions.
public static class Analyzer
{
    public static Manifest Analyze(string targetDir, AnalyzeOptions? options = null)
    {
        options ??= new AnalyzeOptions();
        Action<string> info = options.OnInfo ?? (_ => { });

        PatlasConfig config = ConfigLoader.Load(targetDir);
        IReadOnlyList<string> files = FolderScanner.Scan(targetDir, config.Scan);

        bool ArchiveMatch(string rel) => config.Archive is not null && Globs.Matches(config.Archive.Match, rel);
        bool inventoryOnly = config.Archive?.InventoryOnly == true;

        // Work-item linking is best-effort: resolve the baseUrl and pre-validate tag
        // patterns once; anything unusable degrades to Info and tags still parse.
        string? baseUrl = TextUtil.UsableBaseUrl(config.WorkItems.BaseUrl);
        if (!string.IsNullOrEmpty(config.WorkItems.BaseUrl) && baseUrl is null)
        {
            info($"Work-item links disabled: workItems.baseUrl is a placeholder (\"{config.WorkItems.BaseUrl}\"). Tags are parsed without URLs.");
        }
        else if (string.IsNullOrEmpty(config.WorkItems.BaseUrl))
        {
            info("Work-item links disabled: workItems.baseUrl is not set. Tags are parsed without URLs.");
        }

        var tagPatterns = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach ((string kind, string pattern) in config.WorkItems.TagPatterns)
        {
            try
            {
                _ = new Regex(pattern);
                tagPatterns[kind] = pattern;
            }
            catch (ArgumentException ex)
            {
                info($"Ignoring invalid work-item pattern for \"{kind}\": {ex.Message}");
            }
        }

        // --- Pass 1: one file node per scanned file ----------------------------
        var drafts = new List<NodeDraft>();
        var draftByRelPath = new Dictionary<string, NodeDraft>(StringComparer.Ordinal);
        var idByRelPath = new Dictionary<string, string>(StringComparer.Ordinal);
        var rootByRelPath = new Dictionary<string, YamlMappingNode?>(StringComparer.Ordinal);
        var rawByRelPath = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (string relPath in files)
        {
            string raw = File.ReadAllText(Path.Combine(targetDir, relPath));
            rawByRelPath[relPath] = raw;
            YamlMappingNode? root = null;
            if (IsYaml(relPath))
            {
                root = ParseYamlRoot(raw);
                rootByRelPath[relPath] = root;
            }

            NodeType type = ClassifyFile(relPath, root);
            string id = Ids.FileNodeId(type, relPath);
            NodeStatusRule? statusRule = Globs.FirstMatch(config.NodeStatus, r => r.Match, relPath);
            NodeStatus status = CoerceStatus(statusRule?.Status)
                ?? (ArchiveMatch(relPath) ? NodeStatus.Archived : NodeStatus.Active);

            var draft = new NodeDraft
            {
                Id = id,
                Type = type,
                Path = relPath,
                Title = BaseName(relPath),
                Purpose = TextUtil.HeaderComment(raw),
                ClusterId = Globs.FirstMatch(config.Clusters, c => c.Match, relPath)?.Id,
                Tags = TextUtil.HarvestTags(raw, tagPatterns, baseUrl).ToList(),
                Status = status,
                Source = NodeSource.Parsed,
            };
            if (type == NodeType.Doc)
            {
                draft.Body = raw; // render as formatted Markdown in the viewer (T4b)
            }

            if (type == NodeType.EntryPipeline)
            {
                draft.Trigger = YamlParser.TriggerSummary(root);
            }

            if (IsYaml(relPath))
            {
                draft.Pool = YamlParser.PoolName(root);
            }

            drafts.Add(draft);
            draftByRelPath[relPath] = draft;
            idByRelPath[relPath] = id;
        }

        // --- Pass 2: parse pipelines/templates for steps + edges ---------------
        var steps = new List<Step>();
        var edges = new List<Edge>();
        var envNames = new HashSet<string>(StringComparer.Ordinal);
        var externalRefs = new HashSet<string>(StringComparer.Ordinal);
        var trailByNodeId = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);

        // PowerShell files keyed by basename ("arrslot.psm1") so steps that import or
        // run them (with any path prefix) can be linked via callsScript.
        var scriptIdByBasename = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (NodeDraft d in drafts)
        {
            if (d.Type is NodeType.PsModule or NodeType.PsScript or NodeType.Test && d.Path is not null)
            {
                scriptIdByBasename[Path.GetFileName(Globs.ToPosix(d.Path))] = d.Id;
            }
        }

        // The real environments are the files in the env/ folder (envConfig nodes):
        // e.g. env/dev.yml -> "dev". Only these count as deploy targets, so stray
        // param values like "governance" don't become phantom environments.
        var realEnvs = drafts
            .Where(d => d.Type == NodeType.EnvConfig && d.Path is not null)
            .Select(d => BaseName(d.Path!).ToLowerInvariant())
            .ToHashSet(StringComparer.Ordinal);

        foreach (string relPath in files)
        {
            NodeDraft draft = draftByRelPath[relPath];
            if (draft.Type is not (NodeType.EntryPipeline or NodeType.Template))
            {
                continue;
            }

            if (ArchiveMatch(relPath) && inventoryOnly)
            {
                continue; // inventory-only: node yes, steps no
            }

            rootByRelPath.TryGetValue(relPath, out YamlMappingNode? root);
            ParseResult result = YamlParser.Parse(
                draft.Id, draft.Title, relPath, root, rawByRelPath[relPath], idByRelPath, scriptIdByBasename, realEnvs);

            steps.AddRange(result.Steps);
            edges.AddRange(result.Edges);
            if (result.Parameters.Count > 0)
            {
                draft.Parameters = result.Parameters;
            }
            draft.Flags.AddRange(result.Flags
                .OrderBy(f => f.Severity.ToString(), StringComparer.Ordinal)
                .ThenBy(f => f.Note, StringComparer.Ordinal));
            foreach (string name in result.EnvRefs) envNames.Add(name);
            foreach (string reference in result.ExternalRefs) externalRefs.Add(reference);
            if (draft.Type == NodeType.EntryPipeline)
            {
                trailByNodeId[draft.Id] = result.FlowTrail;
            }
        }

        // --- testedBy: a module/script and its X.Tests.ps1 by matching base name ----
        foreach (NodeDraft test in drafts.Where(d => d.Type == NodeType.Test && d.Path is not null))
        {
            string fileName = Path.GetFileName(Globs.ToPosix(test.Path!)).ToLowerInvariant();
            if (!fileName.EndsWith(".tests.ps1", StringComparison.Ordinal))
            {
                continue;
            }

            string stem = fileName[..^".tests.ps1".Length];
            foreach (string ext in (string[])[".psm1", ".ps1"])
            {
                if (scriptIdByBasename.TryGetValue(stem + ext, out string? moduleId))
                {
                    edges.Add(new Edge
                    {
                        Id = $"testedBy:{moduleId}=>{test.Id}",
                        From = moduleId,
                        To = test.Id,
                        Kind = EdgeKind.TestedBy,
                    });
                    break;
                }
            }
        }

        // --- documents: a README.md documents the other nodes in its folder -------
        foreach (NodeDraft readme in drafts.Where(d =>
            d.Type == NodeType.Doc && d.Path is not null &&
            Path.GetFileName(Globs.ToPosix(d.Path)).Equals("README.md", StringComparison.OrdinalIgnoreCase)))
        {
            string dir = PosixDir(readme.Path!);
            foreach (NodeDraft other in drafts.Where(d => d.Id != readme.Id && d.Path is not null && PosixDir(d.Path) == dir))
            {
                edges.Add(new Edge
                {
                    Id = $"documents:{readme.Id}=>{other.Id}",
                    From = readme.Id,
                    To = other.Id,
                    Kind = EdgeKind.Documents,
                });
            }
        }

        // --- Materialize inferred nodes (environments + external references) ---
        foreach (string name in envNames)
        {
            string id = Ids.EnvNodeId(name);
            if (drafts.Any(d => d.Id == id))
            {
                continue;
            }

            drafts.Add(new NodeDraft
            {
                Id = id,
                Type = NodeType.AdoEnvironment,
                Title = name,
                Purpose = "ADO Environment (inferred from a deployment binding).",
                Status = NodeStatus.Active,
                Source = NodeSource.Inferred,
            });
        }

        foreach (string reference in externalRefs)
        {
            bool crossRepo = reference.Contains('@');
            string id = crossRepo ? $"external:{reference}" : Ids.ExternalNodeId(reference);
            if (drafts.Any(d => d.Id == id))
            {
                continue;
            }

            drafts.Add(new NodeDraft
            {
                Id = id,
                Type = NodeType.External,
                Title = BaseName(reference),
                Purpose = crossRepo
                    ? $"Cross-repo template reference (outside this target): {reference}"
                    : $"Referenced but not found in this target: {reference}",
                Status = NodeStatus.Active,
                Source = NodeSource.Inferred,
            });
        }

        // --- Categories + readable labels (T2a/T2b) ----------------------------
        var envTitleById = drafts
            .Where(d => d.Type == NodeType.AdoEnvironment)
            .ToDictionary(d => d.Id, d => d.Title, StringComparer.Ordinal);

        foreach (NodeDraft draft in drafts)
        {
            string[] deployEnvs = edges
                .Where(e => e.Kind == EdgeKind.DeploysTo && e.From == draft.Id)
                .Select(e => envTitleById.TryGetValue(e.To, out string? t) ? t.ToUpperInvariant() : null)
                .Where(t => t is not null)
                .Select(t => t!)
                .Distinct()
                .OrderBy(t => t, StringComparer.Ordinal)
                .ToArray();

            // Category: config rule wins; else auto (a pipeline that deploys is
            // "deployment", one that runs but doesn't deploy is "policy").
            string? categoryRule = draft.Path is null ? null : Globs.FirstMatch(config.Categories, c => c.Match, draft.Path)?.Category;
            draft.Category = categoryRule
                ?? (draft.Type == NodeType.EntryPipeline ? (deployEnvs.Length > 0 ? "deployment" : "policy") : null);

            // Label: config rule wins; else auto-generate for deployment pipelines.
            string? labelRule = draft.Path is null ? null : Globs.FirstMatch(config.Labels, l => l.Match, draft.Path)?.Label;
            draft.Label = labelRule
                ?? (deployEnvs.Length > 0 ? $"Deploy to {string.Join(", ", deployEnvs)} ({draft.Title})" : null);
        }

        // --- Inferred approval gates (gatedBy) ---------------------------------
        foreach (GateRule gate in config.Gates)
        {
            string envId = Ids.EnvNodeId(gate.Environment);
            NodeDraft? env = drafts.FirstOrDefault(d => d.Id == envId);
            if (env is null)
            {
                continue;
            }

            if (gate.Note is not null)
            {
                env.Purpose = $"{env.Purpose} Gated: {gate.Note}";
            }

            foreach (Edge dep in edges.Where(e => e.Kind == EdgeKind.DeploysTo && e.To == envId).ToArray())
            {
                string gid = $"gatedBy:{dep.From}=>{envId}";
                if (!edges.Any(e => e.Id == gid))
                {
                    edges.Add(new Edge { Id = gid, From = dep.From, To = envId, Kind = EdgeKind.GatedBy });
                }
            }
        }

        // --- Flows: one per entry pipeline -------------------------------------
        var flows = new List<Flow>();
        foreach (NodeDraft draft in drafts)
        {
            if (draft.Type != NodeType.EntryPipeline)
            {
                continue;
            }

            IReadOnlyList<string> trail = trailByNodeId.TryGetValue(draft.Id, out var t) ? t : [draft.Id];
            flows.Add(new Flow
            {
                Id = $"flow:{Ids.Slug(draft.Title)}",
                Title = $"Run: {draft.Title}",
                Trigger = draft.Trigger ?? "manual",
                Path = trail,
            });
        }

        DateTimeOffset now = options.Now ?? DateTimeOffset.UtcNow;
        var manifest = new Manifest
        {
            Target = new TargetInfo
            {
                DisplayName = config.DisplayName,
                Description = config.Description,
                GeneratedAt = now.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture),
                ToolVersion = options.ToolVersion ?? ToolInfo.Version,
            },
            Nodes = drafts
                .OrderBy(d => d.Id, StringComparer.Ordinal)
                .Select(d => d.ToNode())
                .ToArray(),
            Edges = edges
                .OrderBy(e => e.Id, StringComparer.Ordinal)
                .ToArray(),
            Steps = steps, // encounter order is deterministic and preserves reading order
            Flows = flows
                .OrderBy(f => f.Id, StringComparer.Ordinal)
                .ToArray(),
        };

        ManifestValidator.AssertValid(manifest);
        return manifest;
    }

    private static NodeType ClassifyFile(string relPath, YamlMappingNode? root)
    {
        string lower = Globs.ToPosix(relPath).ToLowerInvariant();
        if (lower.EndsWith(".psm1", StringComparison.Ordinal)) return NodeType.PsModule;
        if (lower.EndsWith(".tests.ps1", StringComparison.Ordinal)) return NodeType.Test;
        if (lower.EndsWith(".ps1", StringComparison.Ordinal)) return NodeType.PsScript;
        if (lower.EndsWith(".md", StringComparison.Ordinal)) return NodeType.Doc;
        if (lower.EndsWith(".json", StringComparison.Ordinal)) return NodeType.Data;
        if (IsYaml(relPath)) return YamlParser.ClassifyYaml(relPath, root);
        return NodeType.Data;
    }

    private static bool IsYaml(string relPath)
    {
        string l = relPath.ToLowerInvariant();
        return l.EndsWith(".yml", StringComparison.Ordinal) || l.EndsWith(".yaml", StringComparison.Ordinal);
    }

    private static string PosixDir(string relPath)
    {
        string p = Globs.ToPosix(relPath);
        int slash = p.LastIndexOf('/');
        return slash >= 0 ? p[..slash] : "";
    }

    private static string BaseName(string relPath)
    {
        string p = Globs.ToPosix(relPath);
        int slash = p.LastIndexOf('/');
        string baseName = slash >= 0 ? p[(slash + 1)..] : p;
        int dot = baseName.LastIndexOf('.');
        return dot > 0 ? baseName[..dot] : baseName;
    }

    private static NodeStatus? CoerceStatus(string? status) => status switch
    {
        "active" => NodeStatus.Active,
        "legacy-active" => NodeStatus.LegacyActive,
        "dormant" => NodeStatus.Dormant,
        "archived" => NodeStatus.Archived,
        _ => null,
    };

    private static YamlMappingNode? ParseYamlRoot(string raw)
    {
        try
        {
            var stream = new YamlStream();
            stream.Load(new StringReader(raw));
            return stream.Documents.Count > 0 ? stream.Documents[0].RootNode as YamlMappingNode : null;
        }
        catch
        {
            return null;
        }
    }

    // Mutable draft so flags discovered during parsing can be attached to the node
    // that produced them; converted to an immutable Node at assembly time.
    private sealed class NodeDraft
    {
        public required string Id { get; init; }
        public required NodeType Type { get; init; }
        public string? Path { get; init; }
        public required string Title { get; init; }
        public string? Label { get; set; }
        public required string Purpose { get; set; }
        public string? ClusterId { get; set; }
        public string? Category { get; set; }
        public string? Trigger { get; set; }
        public string? Pool { get; set; }
        public string? Body { get; set; }
        public IReadOnlyList<Parameter>? Parameters { get; set; }
        public List<Tag> Tags { get; set; } = [];
        public List<Flag> Flags { get; } = [];
        public required NodeStatus Status { get; init; }
        public NodeSource? Source { get; init; }

        public Node ToNode() => new()
        {
            Id = Id,
            Type = Type,
            Path = Path,
            Title = Title,
            Label = Label,
            Purpose = Purpose,
            ClusterId = ClusterId,
            Category = Category,
            Trigger = Trigger,
            Pool = Pool,
            Body = Body,
            Parameters = Parameters,
            Tags = Tags,
            Flags = Flags,
            Status = Status,
            Source = Source,
        };
    }
}
