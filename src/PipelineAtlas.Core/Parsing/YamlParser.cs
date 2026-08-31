using System.Text.RegularExpressions;
using YamlDotNet.RepresentationModel;
using PipelineAtlas.Core.Model;
using PipelineAtlas.Core.Util;

namespace PipelineAtlas.Core.Parsing;

// Azure Pipelines YAML -> structure (CLAUDE.md sec 6), scoped to the edges the
// sample fixture exercises end to end:
//   - `- template: <path>`            -> includesTemplate (resolved node, or an
//                                         external node + techdebt flag when missing)
//   - deployment job `environment:`   -> runsOnEnvironment to an inferred adoEnvironment
//   - include param envName/stage     -> deploysTo to that adoEnvironment
//   - top-level `extends:`            -> extends
// Template ${{ parameters.X }} refs resolve against the file's own parameter
// defaults so a template read on its own still names its environment. Deeper rules
// (callsScript, testedBy, artifacts, gatedBy) are seams for later milestones.
public sealed partial class YamlParser
{
    private readonly string _nodeId;
    private readonly string _nodeTitle;
    private readonly string _nodeRelPath;
    private readonly IReadOnlyDictionary<string, string> _idByRelPath;
    private readonly IReadOnlyDictionary<string, string> _scriptIdByBasename; // "arrslot.psm1" -> node id
    private readonly IReadOnlySet<string> _realEnvs; // env names from the env/ folder (lowercased)
    private readonly Dictionary<string, string> _params;
    private readonly string[] _lines; // raw file lines, for pulling comments above a node
    private readonly ParseResult _result = new();
    private readonly HashSet<string> _seenStepIds = new(StringComparer.Ordinal);

    private YamlParser(
        string nodeId,
        string nodeTitle,
        string nodeRelPath,
        YamlMappingNode? root,
        string raw,
        IReadOnlyDictionary<string, string> idByRelPath,
        IReadOnlyDictionary<string, string> scriptIdByBasename,
        IReadOnlySet<string> realEnvs)
    {
        _nodeId = nodeId;
        _nodeTitle = nodeTitle;
        _nodeRelPath = nodeRelPath;
        _idByRelPath = idByRelPath;
        _scriptIdByBasename = scriptIdByBasename;
        _realEnvs = realEnvs;
        _params = ParamDefaults(root);
        _lines = raw.Split('\n');
    }

    public static ParseResult Parse(
        string nodeId,
        string nodeTitle,
        string nodeRelPath,
        YamlMappingNode? root,
        string raw,
        IReadOnlyDictionary<string, string> idByRelPath,
        IReadOnlyDictionary<string, string> scriptIdByBasename,
        IReadOnlySet<string> realEnvs)
    {
        var parser = new YamlParser(nodeId, nodeTitle, nodeRelPath, root, raw, idByRelPath, scriptIdByBasename, realEnvs);
        parser.Run(root);
        return parser._result;
    }

    // A resolved name is a deploy target only if it's a real environment (a file in
    // the env/ folder). Stray param values like "governance" are ignored.
    private bool IsRealEnv(string name) => _realEnvs.Contains(name.ToLowerInvariant());

    // --- classification helpers (used by the analyzer) -----------------------

    /// <summary>entryPipeline / template / envConfig from the file's path + parsed doc.</summary>
    public static NodeType ClassifyYaml(string relPath, YamlMappingNode? root)
    {
        if (Globs.ToPosix(relPath).Split('/').Contains("env"))
        {
            return NodeType.EnvConfig;
        }

        if (root is not null &&
            (Yaml.Has(root, "trigger") || Yaml.Has(root, "pr") || Yaml.Has(root, "schedules")))
        {
            return NodeType.EntryPipeline;
        }

        return NodeType.Template;
    }

    public static string? TriggerSummary(YamlMappingNode? root)
    {
        if (root is null)
        {
            return null;
        }

        YamlNode? trigger = Yaml.Get(root, "trigger");
        if (trigger is YamlScalarNode scalar)
        {
            return scalar.Value is "none" ? "manual" : "CI";
        }

        if (trigger is YamlMappingNode triggerMap)
        {
            IReadOnlyList<string> include = BranchIncludes(triggerMap);
            return include.Count > 0 ? $"CI: {string.Join(", ", include)}" : "CI";
        }

        if (trigger is not null)
        {
            return "CI";
        }

        if (Yaml.Has(root, "schedules"))
        {
            return "schedule";
        }

        return Yaml.Has(root, "pr") ? "PR gate" : null;
    }

    public static string? PoolName(YamlMappingNode? root)
    {
        YamlNode? pool = Yaml.Get(root, "pool");
        return pool switch
        {
            YamlScalarNode s => s.Value,
            YamlMappingNode m => Yaml.GetScalar(m, "name"),
            _ => null,
        };
    }

    // --- top-level walk ------------------------------------------------------

    private void Run(YamlMappingNode? root)
    {
        _result.FlowTrail.Add(_nodeId);
        if (root is null)
        {
            return;
        }

        ExtractParameters(root);

        // extends: -> extends edge
        YamlMappingNode? ext = Yaml.AsMap(Yaml.Get(root, "extends"));
        string? extTemplate = Yaml.GetScalar(ext, "template");
        if (extTemplate is not null)
        {
            AddTemplateEdge(extTemplate, EdgeKind.Extends, null);
        }

        YamlNode? stages = Yaml.Get(root, "stages");
        YamlNode? jobs = Yaml.Get(root, "jobs");
        YamlNode? steps = Yaml.Get(root, "steps");

        if (stages is YamlSequenceNode)
        {
            foreach (YamlNode stage in Yaml.Seq(stages))
            {
                WalkStage(stage);
            }
        }
        else if (jobs is YamlSequenceNode)
        {
            foreach (YamlNode job in Yaml.Seq(jobs))
            {
                WalkJob(job, _nodeTitle, null);
            }
        }
        else if (steps is YamlSequenceNode)
        {
            foreach (YamlNode step in Yaml.Seq(steps))
            {
                WalkStep(step, _nodeTitle, null);
            }
        }
    }

    private void WalkStage(YamlNode stageNode)
    {
        YamlMappingNode? stage = Yaml.AsMap(stageNode);
        if (stage is null)
        {
            return;
        }

        if (TryExpandConditional(stage, child => WalkStage(child)))
        {
            return;
        }

        if (Yaml.GetScalar(stage, "template") is not null)
        {
            HandleInclude(stage, _nodeTitle, null);
            return;
        }

        string name = Yaml.GetScalar(stage, "stage") ?? Yaml.GetScalar(stage, "displayName") ?? "stage";
        string id = UniqueStepId($"{_nodeTitle}/{name}");
        AddStep(id, StepKind.Stage, null, Yaml.GetScalar(stage, "displayName") ?? name, null, stage);
        _result.FlowTrail.Add(id);

        foreach (YamlNode job in Yaml.Seq(Yaml.Get(stage, "jobs")))
        {
            WalkJob(job, id, id);
        }
    }

    private void WalkJob(YamlNode jobNode, string baseId, string? stageStepId)
    {
        YamlMappingNode? job = Yaml.AsMap(jobNode);
        if (job is null)
        {
            return;
        }

        if (TryExpandConditional(job, child => WalkJob(child, baseId, stageStepId)))
        {
            return;
        }

        if (Yaml.GetScalar(job, "template") is not null)
        {
            HandleInclude(job, baseId, stageStepId);
            return;
        }

        string name = Yaml.GetScalar(job, "job")
            ?? Yaml.GetScalar(job, "deployment")
            ?? Yaml.GetScalar(job, "displayName")
            ?? "job";
        string id = UniqueStepId($"{baseId}/{name}");
        AddStep(id, StepKind.Job, stageStepId, Yaml.GetScalar(job, "displayName") ?? name, null, job);

        // deployment job bound to an ADO Environment -> runsOnEnvironment
        string? envName = ResolveExpr(Yaml.Get(job, "environment"));
        if (envName is not null && !envName.Contains("${{") && IsRealEnv(envName))
        {
            _result.EnvRefs.Add(envName);
            AddEdge(_nodeId, Ids.EnvNodeId(envName), EdgeKind.RunsOnEnvironment, id);
        }

        foreach (YamlNode step in CollectJobSteps(job))
        {
            WalkStep(step, id, id);
        }
    }

    private void WalkStep(YamlNode stepNode, string baseId, string? parentId)
    {
        YamlMappingNode? step = Yaml.AsMap(stepNode);
        if (step is null)
        {
            return;
        }

        if (TryExpandConditional(step, child => WalkStep(child, baseId, parentId)))
        {
            return;
        }

        if (Yaml.GetScalar(step, "template") is not null)
        {
            HandleInclude(step, baseId, parentId);
            return;
        }

        string name = StepName(step);
        string id = UniqueStepId($"{baseId}/{Ids.Slug(name)}");
        AddStep(id, StepKind.Step, parentId, name, StepAction(step), step);
        DetectScriptCalls(step);
    }

    // callsScript: a step that runs/imports a PowerShell file in this target. Paths
    // carry inconsistent prefixes ($(Build.SourcesDirectory)\pipelines\scripts\X.psm1,
    // pipelines/scripts/X.ps1, ...), so we match by basename. Node-level edge (no step).
    private void DetectScriptCalls(YamlMappingNode step)
    {
        YamlMappingNode? inputs = Yaml.AsMap(Yaml.Get(step, "inputs"));
        string text = string.Join(
            "\n",
            Yaml.GetScalar(inputs, "filePath"),
            Yaml.GetScalar(inputs, "script"),
            Yaml.GetScalar(step, "script"),
            Yaml.GetScalar(step, "powershell"),
            Yaml.GetScalar(step, "pwsh"));

        foreach (Match m in ScriptRefPattern().Matches(text))
        {
            string basename = m.Value.ToLowerInvariant();
            if (_scriptIdByBasename.TryGetValue(basename, out string? scriptId) && scriptId != _nodeId)
            {
                AddEdge(_nodeId, scriptId, EdgeKind.CallsScript, null);
            }
        }
    }

    // --- template includes ---------------------------------------------------

    private void HandleInclude(YamlMappingNode item, string baseId, string? atStageStepId)
    {
        string rawRef = Yaml.GetScalar(item, "template")!;
        string includeName = PosixBaseName(rawRef);
        string stepId = UniqueStepId($"{baseId}/{Ids.Slug(includeName)}");
        AddStep(stepId, StepKind.TemplateInclude, atStageStepId, includeName, $"template: {rawRef}", item);

        // Resolve ${{ parameters.X }} in the path against this file's own defaults.
        // If it still contains an expression it's a dynamic include we can't resolve
        // statically: the step is recorded, but we don't invent an edge/external node.
        string reference = ResolveExprString(rawRef);
        if (!reference.Contains("${{", StringComparison.Ordinal))
        {
            AddTemplateEdge(reference, EdgeKind.IncludesTemplate, stepId);
        }

        // deploysTo: an include that names a target tier (envName / stage) literally
        YamlMappingNode? parameters = Yaml.AsMap(Yaml.Get(item, "parameters"));
        string? rawTier = Yaml.GetScalar(parameters, "envName") ?? Yaml.GetScalar(parameters, "stage");
        string? tier = rawTier is null ? null : ResolveExprString(rawTier);
        if (tier is not null && !tier.Contains("${{", StringComparison.Ordinal) && IsRealEnv(tier))
        {
            _result.EnvRefs.Add(tier);
            AddEdge(_nodeId, Ids.EnvNodeId(tier), EdgeKind.DeploysTo, atStageStepId ?? stepId);
            _result.FlowTrail.Add(Ids.EnvNodeId(tier)); // the flow reaches the environment it deploys to
        }
    }

    private void AddTemplateEdge(string reference, EdgeKind kind, string? atStepId)
    {
        // Cross-repo template (path@resource) -> always external, keyed by raw ref.
        if (reference.Contains('@'))
        {
            _result.ExternalRefs.Add(reference);
            AddEdge(_nodeId, $"external:{reference}", kind, atStepId);
            return;
        }

        string resolved = ResolveTemplatePath(reference, _nodeRelPath);
        if (_idByRelPath.TryGetValue(resolved, out string? existingId))
        {
            AddEdge(_nodeId, existingId, kind, atStepId);
            _result.FlowTrail.Add(existingId);
            return;
        }

        // Referenced but not present in the target: emit an external node + flag so
        // the edge resolves and the gap is visible (per the confirmed decision).
        _result.ExternalRefs.Add(resolved);
        _result.Flags.Add(new Flag
        {
            Severity = FlagSeverity.Techdebt,
            Note = $"Unresolved template reference: {reference} (resolved to {resolved}, not found in target)",
            StepId = atStepId,
        });
        AddEdge(_nodeId, Ids.ExternalNodeId(resolved), kind, atStepId);
    }

    // --- helpers -------------------------------------------------------------

    private static IReadOnlyList<YamlNode> CollectJobSteps(YamlMappingNode job)
    {
        if (Yaml.Get(job, "steps") is YamlSequenceNode direct)
        {
            return [.. direct.Children];
        }

        YamlMappingNode? strategy = Yaml.AsMap(Yaml.Get(job, "strategy"));
        if (strategy is null)
        {
            return [];
        }

        var collected = new List<YamlNode>();
        foreach (KeyValuePair<YamlNode, YamlNode> style in strategy.Children)
        {
            if (Yaml.AsMap(style.Value) is not { } styleMap)
            {
                continue;
            }

            foreach (KeyValuePair<YamlNode, YamlNode> hook in styleMap.Children)
            {
                if (Yaml.Get(Yaml.AsMap(hook.Value), "steps") is YamlSequenceNode hookSteps)
                {
                    collected.AddRange(hookSteps.Children);
                }
            }
        }

        return collected;
    }

    private static string StepName(YamlMappingNode step)
    {
        if (Yaml.GetScalar(step, "displayName") is { } display) return display;
        if (Yaml.GetScalar(step, "task") is { } task) return task;
        if (Yaml.Has(step, "script")) return "script";
        if (Yaml.Has(step, "powershell")) return "powershell";
        if (Yaml.Has(step, "pwsh")) return "pwsh";
        if (Yaml.Has(step, "bash")) return "bash";
        if (Yaml.Has(step, "download")) return $"download {Yaml.GetScalar(step, "download")}".Trim();
        if (Yaml.Has(step, "checkout")) return $"checkout {Yaml.GetScalar(step, "checkout")}".Trim();
        return "step";
    }

    private static string? StepAction(YamlMappingNode step)
    {
        if (Yaml.GetScalar(step, "task") is { } task) return task;
        if (Yaml.Has(step, "script")) return "script";
        if (Yaml.Has(step, "powershell")) return "powershell";
        if (Yaml.Has(step, "pwsh")) return "pwsh";
        if (Yaml.Has(step, "bash")) return "bash";
        if (Yaml.Has(step, "download")) return $"download: {Yaml.GetScalar(step, "download")}".Trim();
        if (Yaml.Has(step, "publish")) return $"publish: {Yaml.GetScalar(step, "publish")}".Trim();
        if (Yaml.Has(step, "checkout")) return $"checkout: {Yaml.GetScalar(step, "checkout")}".Trim();
        return null;
    }

    private static Dictionary<string, string> ParamDefaults(YamlMappingNode? root)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (YamlNode entry in Yaml.Seq(Yaml.Get(root, "parameters")))
        {
            YamlMappingNode? p = Yaml.AsMap(entry);
            string? name = Yaml.GetScalar(p, "name");
            string? def = Yaml.GetScalar(p, "default");
            if (name is not null && def is not null)
            {
                result[name] = def;
            }
        }

        return result;
    }

    /// <summary>Resolve ${{ parameters.NAME }} / ${{ parameters['NAME'] }} against the context.</summary>
    private string? ResolveExpr(YamlNode? node)
    {
        string? s = node switch
        {
            YamlScalarNode scalar => scalar.Value,
            YamlMappingNode map => Yaml.GetScalar(map, "name"),
            _ => null,
        };
        return s is null ? null : ResolveExprString(s);
    }

    private string ResolveExprString(string s)
    {
        s = ExprDot().Replace(s, m => _params.TryGetValue(m.Groups[1].Value, out string? v) ? v : m.Value);
        s = ExprIndex().Replace(s, m => _params.TryGetValue(m.Groups[1].Value, out string? v) ? v : m.Value);
        return s.Trim();
    }

    // Azure Pipelines wraps lists of stages/jobs/steps in ${{ if/each/else }} blocks:
    //   steps:
    //   - ${{ if eq(...) }}:
    //     - <step>
    // Such a list item is a mapping whose keys are all expressions. Recurse into each
    // sequence value at the same level so nested includes/jobs/steps aren't missed.
    private static bool TryExpandConditional(YamlMappingNode map, Action<YamlNode> recurse)
    {
        if (map.Children.Count == 0)
        {
            return false;
        }

        foreach (KeyValuePair<YamlNode, YamlNode> kv in map.Children)
        {
            if (kv.Key is not YamlScalarNode k ||
                k.Value is null ||
                !k.Value.StartsWith("${{", StringComparison.Ordinal))
            {
                return false;
            }
        }

        foreach (KeyValuePair<YamlNode, YamlNode> kv in map.Children)
        {
            if (kv.Value is YamlSequenceNode seq)
            {
                foreach (YamlNode item in seq.Children)
                {
                    recurse(item);
                }
            }
        }

        return true; // consumed: an expression-keyed block is never a plain stage/job/step
    }

    private static string ResolveTemplatePath(string reference, string fromRelPath)
    {
        string r = Globs.ToPosix(reference);
        if (r.StartsWith("./", StringComparison.Ordinal))
        {
            r = r[2..];
        }

        if (r.StartsWith('/'))
        {
            return NormalizePosix(r[1..]);
        }

        string dir = PosixDirName(Globs.ToPosix(fromRelPath));
        return NormalizePosix(dir.Length == 0 ? r : $"{dir}/{r}");
    }

    private void AddStep(string id, StepKind kind, string? parentId, string name, string? action, YamlNode? source)
    {
        _result.Steps.Add(new Step
        {
            Id = id,
            NodeId = _nodeId,
            ParentId = parentId,
            Kind = kind,
            Name = name,
            Doc = CommentAbove(source),
            Action = action,
            Tags = [],
        });
    }

    /// <summary>Extract the pipeline/template's declared parameters, each described by the comment above it.</summary>
    private void ExtractParameters(YamlMappingNode? root)
    {
        foreach (YamlNode entry in Yaml.Seq(Yaml.Get(root, "parameters")))
        {
            YamlMappingNode? p = Yaml.AsMap(entry);
            string? name = Yaml.GetScalar(p, "name");
            if (name is null)
            {
                continue;
            }

            string description = CommentAbove(p);
            _result.Parameters.Add(new Parameter
            {
                Name = name,
                Type = Yaml.GetScalar(p, "type"),
                Default = Yaml.GetScalar(p, "default"),
                Description = description.Length > 0 ? description : null,
            });
        }
    }

    /// <summary>The contiguous block of `#` comment lines immediately above a node, joined to one line.</summary>
    private string CommentAbove(YamlNode? node)
    {
        if (node is null)
        {
            return "";
        }

        var collected = new List<string>();
        // node.Start.Line is 1-based; the line directly above is index (Line - 2).
        for (int i = (int)node.Start.Line - 2; i >= 0 && i < _lines.Length; i--)
        {
            Match m = CommentLinePattern().Match(_lines[i].TrimEnd('\r'));
            if (!m.Success)
            {
                break; // stop at the first non-comment line (keeps the block tight to this node)
            }

            collected.Add(m.Groups[1].Value.TrimEnd());
        }

        collected.Reverse();
        // Preserve the comment's line/paragraph structure so it stays readable
        // (the viewer renders it with pre-wrap); only trim blank edges.
        return string.Join("\n", collected).Trim('\n', ' ');
    }

    private void AddEdge(string from, string to, EdgeKind kind, string? atStepId)
    {
        string id = $"{CamelCase(kind)}:{from}=>{to}" + (atStepId is not null ? $"@{atStepId}" : "");
        if (_result.Edges.Any(e => e.Id == id))
        {
            return;
        }

        _result.Edges.Add(new Edge
        {
            Id = id,
            From = from,
            To = to,
            Kind = kind,
            AtStepId = atStepId,
        });
    }

    private string UniqueStepId(string candidate)
    {
        string id = candidate;
        int n = 2;
        while (_seenStepIds.Contains(id))
        {
            id = $"{candidate}-{n++}";
        }

        _seenStepIds.Add(id);
        return id;
    }

    private static IReadOnlyList<string> BranchIncludes(YamlMappingNode trigger)
    {
        YamlNode? branches = Yaml.Get(trigger, "branches");
        if (branches is YamlSequenceNode seq)
        {
            return seq.Children.Select(n => Yaml.Scalar(n) ?? "").ToArray();
        }

        if (Yaml.Get(Yaml.AsMap(branches), "include") is YamlSequenceNode include)
        {
            return include.Children.Select(n => Yaml.Scalar(n) ?? "").ToArray();
        }

        return [];
    }

    private static string CamelCase(EdgeKind kind)
    {
        string name = kind.ToString();
        return char.ToLowerInvariant(name[0]) + name[1..];
    }

    private static string PosixBaseName(string path)
    {
        string p = Globs.ToPosix(path);
        int slash = p.LastIndexOf('/');
        return slash >= 0 ? p[(slash + 1)..] : p;
    }

    private static string PosixDirName(string path)
    {
        int slash = path.LastIndexOf('/');
        return slash >= 0 ? path[..slash] : "";
    }

    private static string NormalizePosix(string path)
    {
        var stack = new List<string>();
        foreach (string segment in path.Split('/'))
        {
            if (segment is "" or ".")
            {
                continue;
            }

            if (segment == ".." && stack.Count > 0 && stack[^1] != "..")
            {
                stack.RemoveAt(stack.Count - 1);
            }
            else if (segment == ".." && stack.Count == 0)
            {
                // stay at root; drop leading ..
            }
            else
            {
                stack.Add(segment);
            }
        }

        return string.Join('/', stack);
    }

    [GeneratedRegex(@"^\s*#\s?(.*)$")]
    private static partial Regex CommentLinePattern();

    [GeneratedRegex(@"[A-Za-z0-9_.-]+\.psm1|[A-Za-z0-9_.-]+\.ps1")]
    private static partial Regex ScriptRefPattern();

    [GeneratedRegex(@"\$\{\{\s*parameters\.([A-Za-z0-9_]+)\s*\}\}")]
    private static partial Regex ExprDot();

    [GeneratedRegex(@"\$\{\{\s*parameters\[['""]([^'""]+)['""]\]\s*\}\}")]
    private static partial Regex ExprIndex();
}
