// Mirror of the C# manifest contract (PipelineAtlas.Core Model). The viewer reads
// ONLY this — never a live repo or Azure DevOps (CLAUDE.md sec 8.4).

export type NodeType =
  | "entryPipeline"
  | "template"
  | "envConfig"
  | "psModule"
  | "psScript"
  | "test"
  | "doc"
  | "data"
  | "adoEnvironment"
  | "adoResource"
  | "external";

export type NodeStatus = "active" | "legacy-active" | "dormant" | "archived";

export interface Tag {
  kind: "story" | "feature" | "bug";
  id: string;
  url?: string;
}

export interface Flag {
  severity: "secret" | "hardcode" | "techdebt" | "duplication" | "antipattern";
  note: string;
  stepId?: string;
}

export interface Parameter {
  name: string;
  type?: string;
  default?: string;
  description?: string;
}

export interface Node {
  id: string;
  type: NodeType;
  path?: string;
  title: string;
  label?: string; // readable display name (T2b)
  purpose: string;
  clusterId?: string;
  category?: string; // "deployment" | "policy" | ... (T2a)
  trigger?: string;
  pool?: string;
  body?: string; // markdown content, for doc (.md) nodes
  parameters?: Parameter[];
  tags: Tag[];
  flags: Flag[];
  status: NodeStatus;
  source?: "parsed" | "inferred";
}

/** The name to show for a node — its readable label if present, else its title. */
export function displayName(n: Node): string {
  return n.label ?? n.title;
}

export type EdgeKind =
  | "extends"
  | "includesTemplate"
  | "callsScript"
  | "runsOnEnvironment"
  | "deploysTo"
  | "consumesArtifact"
  | "producesArtifact"
  | "gatedBy"
  | "testedBy"
  | "referencesExternal"
  | "documents";

export interface Edge {
  id: string;
  from: string;
  to: string;
  kind: EdgeKind;
  atStepId?: string;
  tags?: Tag[];
}

export type StepKind = "stage" | "job" | "step" | "templateInclude";

export interface Step {
  id: string;
  nodeId: string;
  parentId?: string;
  kind: StepKind;
  name: string;
  doc: string;
  action?: string;
  externalDeps?: string[];
  tags: Tag[];
}

export interface Flow {
  id: string;
  title: string;
  trigger: string;
  path: string[];
  notes?: string;
}

export interface Manifest {
  target: {
    displayName: string;
    description?: string;
    generatedAt: string;
    toolVersion: string;
  };
  nodes: Node[];
  edges: Edge[];
  steps: Step[];
  flows: Flow[];
}

/** Load the manifest served next to the app (dev: public/, prod: the CLI server). */
export async function loadManifest(): Promise<Manifest> {
  const res = await fetch("./manifest.json", { cache: "no-store" });
  if (!res.ok) {
    throw new Error(`Could not load manifest.json (${res.status})`);
  }
  return (await res.json()) as Manifest;
}

// --- small derived helpers ---------------------------------------------------

export const NONE_CLUSTER = "(unclustered)";

export interface ClusterSummary {
  id: string;
  label: string;
  nodeIds: Set<string>;
}

/** Group nodes into clusters (fills a synthetic bucket for unclustered nodes). */
export function clustersOf(m: Manifest): ClusterSummary[] {
  const byId = new Map<string, ClusterSummary>();
  for (const node of m.nodes) {
    const id = node.clusterId ?? NONE_CLUSTER;
    let c = byId.get(id);
    if (!c) {
      c = { id, label: id, nodeIds: new Set() };
      byId.set(id, c);
    }
    c.nodeIds.add(node.id);
  }
  return [...byId.values()].sort((a, b) => a.label.localeCompare(b.label));
}

export function nodeById(m: Manifest): Map<string, Node> {
  return new Map(m.nodes.map((n) => [n.id, n]));
}

/** Distinct tags across the manifest, sorted for the filter control. */
export function allTags(m: Manifest): Tag[] {
  const seen = new Map<string, Tag>();
  for (const n of m.nodes) {
    for (const t of n.tags) seen.set(`${t.kind}:${t.id}`, t);
  }
  return [...seen.values()].sort(
    (a, b) => a.kind.localeCompare(b.kind) || Number(a.id) - Number(b.id),
  );
}
