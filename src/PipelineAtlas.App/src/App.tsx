import { useEffect, useMemo, useRef, useState, type MouseEvent } from "react";
import {
  Background,
  Controls,
  MarkerType,
  MiniMap,
  ReactFlow,
  type Edge as FlowEdge,
  type Node as FlowNode,
  type Viewport,
} from "@xyflow/react";
import { displayName, loadManifest, NONE_CLUSTER, type Manifest, type Node, type Step } from "./manifest";
import { layout } from "./layout";
import { DocPanel } from "./DocPanel";
import { StepPanel } from "./StepPanel";
import { FullView, type FullContent } from "./FullView";
import { boxStyle, edgeColor, statusStyle, STEP_KIND_COLOR, TYPE_COLOR, TYPE_LABEL } from "./theme";

type Level = "home" | "deployment" | "policy" | "subsystem" | "allfiles" | "files" | "steps";

interface ViewState {
  level: Level;
  cluster: string | null;
  pipeline: string | null;
}

interface HistoryEntry {
  view: ViewState;
  vp: Viewport; // the zoom/pan when we left this view, so Back can restore it
}

interface Menu {
  x: number;
  y: number;
  nodeId: string;
}

const GATE_ICON = "🛑"; // an environment awaiting human approval before continuing
// ▶ a pipeline with no automatic trigger — started by hand. U+FE0E forces the
// text-presentation triangle (plain black glyph, no border), not the ▶️ emoji.
const MANUAL_START_ICON = "▶︎";

// An entry pipeline with no automatic trigger (CI/PR/schedule) — a human starts it.
function isManualStart(n: Node): boolean {
  return n.type === "entryPipeline" && n.trigger === "manual";
}

function clusterKey(node: Node): string {
  if (node.clusterId) return node.clusterId;
  if (node.type === "adoEnvironment") return "environments";
  if (node.type === "external") return "external";
  return NONE_CLUSTER;
}

export function App() {
  const [manifest, setManifest] = useState<Manifest | null>(null);
  const [error, setError] = useState<string | null>(null);

  const [view, setView] = useState<ViewState>({ level: "home", cluster: null, pipeline: null });
  const [history, setHistory] = useState<HistoryEntry[]>([]);
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [stepId, setStepId] = useState<string | null>(null);
  const [tagFilter, setTagFilter] = useState<string>("");
  const [menu, setMenu] = useState<Menu | null>(null);
  const [fullView, setFullView] = useState<FullContent | null>(null);
  const [panelWidth, setPanelWidth] = useState(340);
  const vpRef = useRef<Viewport>({ x: 0, y: 0, zoom: 1 }); // current viewport
  const restoreVp = useRef<Viewport | null>(null); // viewport to apply on next mount (Back)

  useEffect(() => {
    loadManifest().then(setManifest).catch((e) => setError(String(e)));
  }, []);

  const byId = useMemo(() => new Map((manifest?.nodes ?? []).map((n) => [n.id, n])), [manifest]);

  const stepsByNode = useMemo(() => {
    const map = new Map<string, Step[]>();
    for (const s of manifest?.steps ?? []) {
      (map.get(s.nodeId) ?? map.set(s.nodeId, []).get(s.nodeId)!).push(s);
    }
    return map;
  }, [manifest]);

  const gatedEnvs = useMemo(() => {
    const s = new Set<string>();
    for (const e of manifest?.edges ?? []) if (e.kind === "gatedBy") s.add(e.to);
    return s;
  }, [manifest]);

  // Pipelines/templates that contain a step which pauses the run for a human
  // (ManualValidation / ManualIntervention) — the in-file counterpart to env gates.
  const manualPauseNodes = useMemo(() => {
    const s = new Set<string>();
    for (const st of manifest?.steps ?? []) if (st.manualPause) s.add(st.nodeId);
    return s;
  }, [manifest]);

  // Everything that stops an automated run for a human: environment approvals +
  // in-pipeline manual-pause steps. Drives the 🛑 node badge across all views.
  const stops = useMemo(
    () => new Set<string>([...gatedEnvs, ...manualPauseNodes]),
    [gatedEnvs, manualPauseNodes]);

  const clusters = useMemo(() => {
    const map = new Map<string, { id: string; label: string; count: number }>();
    for (const n of manifest?.nodes ?? []) {
      const key = clusterKey(n);
      const c = map.get(key) ?? { id: key, label: key, count: 0 };
      c.count += 1;
      map.set(key, c);
    }
    return [...map.values()].sort((a, b) => a.label.localeCompare(b.label));
  }, [manifest]);

  const tags = useMemo(() => {
    const seen = new Map<string, string>();
    for (const n of manifest?.nodes ?? []) for (const t of n.tags) seen.set(`${t.kind}:${t.id}`, `${t.kind} ${t.id}`);
    return [...seen.entries()].sort((a, b) => a[1].localeCompare(b[1], undefined, { numeric: true }));
  }, [manifest]);

  const counts = useMemo(() => {
    let deployment = 0;
    let policy = 0;
    for (const n of manifest?.nodes ?? []) {
      if (n.type !== "entryPipeline") continue;
      if (n.category === "deployment") deployment += 1;
      else if (n.category === "policy") policy += 1;
    }
    return { deployment, policy };
  }, [manifest]);

  const passesTag = (n: Node) => !tagFilter || n.tags.some((t) => `${t.kind}:${t.id}` === tagFilter);

  const { flowNodes, flowEdges } = useMemo(() => {
    if (!manifest) return { flowNodes: [] as FlowNode[], flowEdges: [] as FlowEdge[] };
    let r: { nodes: FlowNode[]; edges: FlowEdge[] };
    if (view.level === "deployment") r = buildDeploymentView(manifest, stops);
    else if (view.level === "policy") r = buildPolicyView(manifest, selectedId, passesTag, stops);
    else if (view.level === "allfiles") r = buildAllFilesView(manifest, clusters, selectedId, passesTag, stops);
    else if (view.level === "subsystem") r = buildClusterView(manifest, clusters, tagFilter, byId);
    else if (view.level === "steps") r = buildStepsView(stepsByNode.get(view.pipeline ?? "") ?? [], stepId);
    else if (view.level === "files") r = buildFilesView(manifest, view.cluster, selectedId, passesTag, stops);
    else r = { nodes: [], edges: [] };
    return { flowNodes: r.nodes, flowEdges: r.edges };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [manifest, view, tagFilter, selectedId, stepId]);

  if (error) return <div className="status-msg">Failed to load: {error}</div>;
  if (!manifest) return <div className="status-msg">Loading manifest…</div>;

  const selectedNode = selectedId ? byId.get(selectedId) ?? null : null;
  const selectedStep = stepId ? (stepsByNode.get(view.pipeline ?? "") ?? []).find((s) => s.id === stepId) ?? null : null;
  const pipelineNode = view.pipeline ? byId.get(view.pipeline) : null;

  // Facts-from-code: a concise, always-accurate summary derived from parsed
  // relationships (no comments involved), for the selected node (T4 "what it does").
  const facts: string[] = selectedNode ? computeFacts(manifest, selectedNode, byId, stepsByNode) : [];
  const docsFor = (n: Node | null | undefined): { title: string; body: string }[] => {
    if (!n) return [];
    const out: { title: string; body: string }[] = [];
    if (n.type === "doc" && n.body) out.push({ title: n.title, body: n.body });
    for (const e of manifest.edges) {
      if (e.kind === "documents" && e.to === n.id) {
        const d = byId.get(e.from);
        if (d?.body) out.push({ title: d.title, body: d.body });
      }
    }
    return out;
  };
  const relatedDocs = docsFor(selectedNode);

  const go = (next: ViewState) => {
    setHistory((h) => [...h, { view, vp: vpRef.current }]);
    setView(next);
    setSelectedId(null);
    setStepId(null);
    setMenu(null);
  };
  const back = () => setHistory((h) => {
    if (h.length === 0) return h;
    const entry = h[h.length - 1]!;
    restoreVp.current = entry.vp; // restore the zoom/pan we had there
    setView(entry.view);
    setSelectedId(null);
    setStepId(null);
    return h.slice(0, -1);
  });
  const drill = (nodeId: string) => { if (stepsByNode.has(nodeId)) go({ level: "steps", cluster: view.cluster, pipeline: nodeId }); };

  // The file a node maps to: its own path, or (for an inferred environment) its
  // env/<name>.yml config. Used by View file / Ctrl+Click / Shift+Click.
  const fileFor = (n: Node | null | undefined): string | undefined => {
    if (!n) return undefined;
    if (n.path) return n.path;
    if (n.type === "adoEnvironment") {
      return manifest.nodes.find((x) => x.type === "envConfig" && (x.path ?? "").toLowerCase().endsWith(`/${n.title.toLowerCase()}.yml`))?.path;
    }
    return undefined;
  };

  const onNodeClick = (e: MouseEvent, fn: FlowNode) => {
    setMenu(null);
    if ((fn.data as { kind?: string }).kind === "cluster") { go({ level: "files", cluster: (fn.data as { clusterId: string }).clusterId, pipeline: null }); return; }
    // Ctrl/Cmd or Shift + click opens the raw file in a new tab.
    if (e.ctrlKey || e.metaKey || e.shiftKey) {
      const f = view.level === "steps" ? pipelineNode?.path : fileFor(byId.get(fn.id));
      if (f) { viewFile(f); return; }
    }
    if (view.level === "steps") setStepId(fn.id);
    else setSelectedId(fn.id);
  };
  const onNodeDoubleClick = (_: unknown, fn: FlowNode) => {
    setMenu(null);
    if ((fn.data as { kind?: string }).kind === "cluster") { go({ level: "files", cluster: (fn.data as { clusterId: string }).clusterId, pipeline: null }); return; }
    if (view.level === "steps") {
      const step = (stepsByNode.get(view.pipeline ?? "") ?? []).find((s) => s.id === fn.id);
      const target = step && manifest.edges.find((e) => e.atStepId === step.id && e.kind === "includesTemplate")?.to;
      if (target && stepsByNode.has(target)) go({ level: "steps", cluster: view.cluster, pipeline: target });
      return;
    }
    // Drill if there are steps; otherwise navigate to the node's file.
    if (stepsByNode.has(fn.id)) drill(fn.id);
    else { const f = fileFor(byId.get(fn.id)); if (f) viewFile(f); }
  };
  const onNodeContextMenu = (e: MouseEvent, fn: FlowNode) => {
    e.preventDefault();
    if ((fn.data as { kind?: string }).kind === "cluster") return;
    setMenu({ x: e.clientX, y: e.clientY, nodeId: fn.id });
  };

  const viewFile = (path: string) => { window.open(`./source?path=${encodeURIComponent(path)}`, "_blank"); setMenu(null); };

  // Context menu is aware of where you are: file/pipeline nodes vs. step nodes.
  // Any file-backed target offers "View file"; drill actions carry a double-click hint.
  const buildMenuItems = (): MenuItem[] => {
    if (!menu) return [];
    const items: MenuItem[] = [];
    if (view.level === "steps") {
      const step = (stepsByNode.get(view.pipeline ?? "") ?? []).find((s) => s.id === menu.nodeId);
      const targetId = step && manifest.edges.find((e) => e.atStepId === step.id && e.kind === "includesTemplate")?.to;
      const targetNode = targetId ? byId.get(targetId) : null;
      items.push({ label: "Step details", onClick: () => { setStepId(menu.nodeId); setMenu(null); } });
      if (targetId && stepsByNode.has(targetId)) {
        items.push({ label: "Open template steps →", hint: "double-click", onClick: () => go({ level: "steps", cluster: view.cluster, pipeline: targetId }) });
      }
      const filePath = targetNode?.path ?? pipelineNode?.path;
      if (filePath) items.push({ label: "View file", hint: "Ctrl+Click", onClick: () => viewFile(filePath) });
    } else {
      const node = byId.get(menu.nodeId);
      const filePath = fileFor(node);
      items.push({ label: "Details", onClick: () => { setSelectedId(menu.nodeId); setMenu(null); } });
      if (stepsByNode.has(menu.nodeId)) items.push({ label: "Open steps →", hint: "double-click", onClick: () => drill(menu.nodeId) });
      if (filePath) items.push({ label: node?.type === "adoEnvironment" ? "View config file" : "View file", hint: "Ctrl+Click", onClick: () => viewFile(filePath) });
    }
    return items;
  };

  // Environment nodes: which pipelines deploy here + their env config file.
  let envExtra: React.ReactNode;
  let sourcePath = selectedNode?.path;
  if (selectedNode?.type === "adoEnvironment") {
    const deployers = manifest.edges.filter((e) => e.kind === "deploysTo" && e.to === selectedNode.id)
      .map((e) => byId.get(e.from)).filter((n): n is Node => !!n);
    const gated = manifest.edges.some((e) => e.kind === "gatedBy" && e.to === selectedNode.id);
    const cfg = manifest.nodes.find((n) => n.type === "envConfig" && (n.path ?? "").toLowerCase().endsWith(`/${selectedNode.title.toLowerCase()}.yml`));
    if (cfg?.path) sourcePath = cfg.path;
    envExtra = (
      <>
        {gated
          ? <p className="purpose">{GATE_ICON} This environment requires recorded human approval before a deploy proceeds.</p>
          : deployers.length > 0 && <p className="muted">No approval gate is declared for this environment.</p>}
        {deployers.length > 0 && (
          <p className="muted" style={{ marginTop: 6 }}>
            Approval gates come from <b>.patlas.json</b> — they live in Azure DevOps, not the pipeline files.
            Planned: read them live from Azure DevOps when configured, or infer them from the target's docs via AI.
          </p>
        )}
        {cfg?.path && (
          <div className="row"><span className="k">Config</span>
            <a href={`./source?path=${encodeURIComponent(cfg.path)}`} target="_blank" rel="noreferrer">{cfg.path} ↗</a></div>
        )}
        {deployers.length > 0 && (
          <>
            <div className="section-title">Deployed to by</div>
            <ul className="facts">{deployers.map((d) => <li key={d.id}>{displayName(d)}</li>)}</ul>
          </>
        )}
      </>
    );
  }

  const startResize = (e: MouseEvent) => {
    e.preventDefault();
    const startX = e.clientX;
    const startW = panelWidth;
    const onMove = (ev: globalThis.MouseEvent) => setPanelWidth(Math.min(760, Math.max(260, startW + (startX - ev.clientX))));
    const onUp = () => { window.removeEventListener("mousemove", onMove); window.removeEventListener("mouseup", onUp); };
    window.addEventListener("mousemove", onMove);
    window.addEventListener("mouseup", onUp);
  };

  const btn = (level: Level, label: string) => (
    <button className={`btn${view.level === level ? " active" : ""}`}
      onClick={() => go({ level, cluster: null, pipeline: null })}>{label}</button>
  );

  return (
    <div className="app" onClick={() => setMenu(null)}>
      <header className="header">
        <h1>{manifest.target.displayName}</h1>
        {manifest.target.description && <span className="desc">{manifest.target.description}</span>}
        <span className="spacer" />
        <span className="meta">{manifest.nodes.length} nodes · {manifest.edges.length} edges · {manifest.flows.length} flows</span>
      </header>

      <div className="toolbar">
        {btn("home", "Home")}
        {btn("deployment", "Deployment")}
        {btn("policy", "Policy")}
        {btn("subsystem", "Subsystems")}
        <button className="btn" onClick={back} disabled={history.length === 0}>← Back</button>
        <span className="crumb">
          {view.level === "files" && <>› <b>{view.cluster ?? "All files"}</b></>}
          {view.level === "steps" && <>› <b>Steps: {pipelineNode ? displayName(pipelineNode) : view.pipeline}</b></>}
        </span>
        <span className="spacer" />
        <label style={{ fontSize: 12, color: "var(--muted)" }}>Work item</label>
        <select value={tagFilter} onChange={(e) => setTagFilter(e.target.value)}>
          <option value="">All</option>
          {tags.map(([key, label]) => <option key={key} value={key}>{label}</option>)}
        </select>
      </div>

      {view.level !== "home" && (
        <div className="toolbar">
          <div className="legend">
            {view.level === "steps"
              ? (["stage", "job", "step", "templateInclude"] as const).map((k) => (
                  <span className="item" key={k}><span className="swatch" style={{ background: STEP_KIND_COLOR[k] }} />{k}</span>))
              : [...new Set(manifest.nodes.map((n) => n.type))].map((t) => (
                  <span className="item" key={t}><span className="swatch" style={{ background: TYPE_COLOR[t] }} />{TYPE_LABEL[t]}</span>))}
            {["deployment", "policy", "files", "allfiles", "steps"].includes(view.level) && <span className="item">{GATE_ICON} stops for a human</span>}
            {["deployment", "policy", "files", "allfiles"].includes(view.level) && <span className="item">{MANUAL_START_ICON} manual start</span>}
          </div>
        </div>
      )}

      <div className="body" style={{ "--panel-width": `${panelWidth}px` } as React.CSSProperties}>
        {view.level === "home" ? (
          <div className="chooser">
            <h2>Choose a view</h2>
            <div className="cards">
              <button className="card" onClick={() => go({ level: "deployment", cluster: null, pipeline: null })}>
                <div className="card-title">Deployment pipelines</div>
                <div className="card-count">{counts.deployment}</div>
                <div className="card-desc">Build → (test) → deploy code to environments. The promotion spine.</div>
              </button>
              <button className="card" onClick={() => go({ level: "policy", cluster: null, pipeline: null })}>
                <div className="card-title">Policy pipelines</div>
                <div className="card-count">{counts.policy}</div>
                <div className="card-desc">Enforce rules on feature branches — PR builds, naming, gates, scans.</div>
              </button>
              <button className="card" onClick={() => go({ level: "subsystem", cluster: null, pipeline: null })}>
                <div className="card-title">Subsystems</div>
                <div className="card-count">{clusters.length}</div>
                <div className="card-desc">The subsystems and how they connect. Drill to files and steps.</div>
              </button>
              <button className="card" onClick={() => go({ level: "allfiles", cluster: null, pipeline: null })}>
                <div className="card-title">All files</div>
                <div className="card-count">{manifest.nodes.filter((n) => n.path).length}</div>
                <div className="card-desc">Every file, grouped by subsystem — a browsable index.</div>
              </button>
            </div>
          </div>
        ) : (
          <div className="canvas">
            <ReactFlow
              key={`${view.level}:${view.cluster ?? "all"}:${view.pipeline ?? "-"}`}
              nodes={flowNodes} edges={flowEdges}
              onNodeClick={onNodeClick} onNodeDoubleClick={onNodeDoubleClick}
              onNodeContextMenu={onNodeContextMenu}
              onMove={(_, vp) => { vpRef.current = vp; }}
              onInit={() => { restoreVp.current = null; }}
              fitView={!restoreVp.current}
              fitViewOptions={{ padding: 0.2, maxZoom: 1.2 }}
              defaultViewport={restoreVp.current ?? undefined}
              nodesDraggable={false} zoomOnDoubleClick={false}
              minZoom={0.05} maxZoom={2.5}
              proOptions={{ hideAttribution: true }}
            >
              <Background /><Controls /><MiniMap pannable zoomable />
            </ReactFlow>
            {menu && <ContextMenu menu={menu} items={buildMenuItems()} />}
          </div>
        )}
        {view.level !== "home" && <div className="resizer" onMouseDown={startResize} />}
        {view.level === "steps" ? (
          <StepPanel step={selectedStep} sourcePath={pipelineNode?.path} docs={docsFor(pipelineNode)} openFull={setFullView} />
        ) : view.level === "home" ? null : (
          <DocPanel node={selectedNode} facts={facts} docs={relatedDocs} extra={envExtra} sourcePath={sourcePath}
            stepCount={selectedNode ? stepsByNode.get(selectedNode.id)?.length : undefined}
            onOpenSteps={selectedNode ? () => drill(selectedNode.id) : undefined} openFull={setFullView} />
        )}
      </div>
      {fullView && <FullView content={fullView} onClose={() => setFullView(null)} />}
    </div>
  );
}

interface MenuItem {
  label: string;
  hint?: string;
  onClick: () => void;
}

function ContextMenu({ menu, items }: { menu: Menu; items: MenuItem[] }) {
  return (
    <ul className="ctxmenu" style={{ left: menu.x, top: menu.y }} onClick={(e) => e.stopPropagation()}>
      {items.map((it) => (
        <li key={it.label} onClick={it.onClick}>
          {it.label}
          {it.hint && <span className="menu-hint">{it.hint}</span>}
        </li>
      ))}
    </ul>
  );
}

// --- facts-from-code ---------------------------------------------------------

function computeFacts(m: Manifest, n: Node, byId: Map<string, Node>, stepsByNode: Map<string, Step[]>): string[] {
  const name = (id: string) => byId.get(id) ? displayName(byId.get(id)!) : id.split(":").pop() ?? id;
  const out = m.edges.filter((e) => e.from === n.id);
  const inc = m.edges.filter((e) => e.to === n.id);
  const facts: string[] = [];
  const list = (kind: string) => out.filter((e) => e.kind === kind).map((e) => name(e.to));

  if (isManualStart(n)) facts.push(`${MANUAL_START_ICON} Started manually — no automatic trigger (CI/PR/schedule)`);
  const deploys = list("deploysTo");
  if (deploys.length) facts.push(`Deploys to ${[...new Set(deploys)].join(", ")}`);
  const gated = out.filter((e) => e.kind === "gatedBy").map((e) => name(e.to));
  if (gated.length) facts.push(`Requires approval at ${[...new Set(gated)].join(", ")}`);
  const pauses = (stepsByNode.get(n.id) ?? []).filter((s) => s.manualPause);
  if (pauses.length) facts.push(`${GATE_ICON} Pauses for manual approval (${pauses.length} step${pauses.length > 1 ? "s" : ""})`);
  const runsOn = list("runsOnEnvironment");
  if (runsOn.length) facts.push(`Runs on environment ${[...new Set(runsOn)].join(", ")}`);
  const includes = list("includesTemplate");
  if (includes.length) facts.push(`Includes ${includes.length} template${includes.length > 1 ? "s" : ""}: ${includes.join(", ")}`);
  const calls = list("callsScript");
  if (calls.length) facts.push(`Calls ${calls.length} script${calls.length > 1 ? "s" : ""}: ${calls.join(", ")}`);
  const tests = out.filter((e) => e.kind === "testedBy").map((e) => name(e.to));
  if (tests.length) facts.push(`Tested by ${tests.join(", ")}`);
  const testedByIn = inc.filter((e) => e.kind === "testedBy").map((e) => name(e.from));
  if (testedByIn.length) facts.push(`Tests ${testedByIn.join(", ")}`);

  const steps = stepsByNode.get(n.id);
  if (steps?.length) {
    const stages = steps.filter((s) => s.kind === "stage").length;
    facts.push(`${steps.length} steps${stages ? ` across ${stages} stage${stages > 1 ? "s" : ""}` : ""}`);
  }
  return facts;
}

// --- view builders -----------------------------------------------------------

// Prefix a node's label with its human-interaction markers: 🛑 where an
// automated run stops for approval, ▶︎ where a pipeline must be started by hand.
function gatedLabel(n: Node, gated: Set<string>): string {
  const prefix =
    (gated.has(n.id) ? `${GATE_ICON} ` : "") +
    (isManualStart(n) ? `${MANUAL_START_ICON} ` : "");
  return prefix + displayName(n);
}

function buildDeploymentView(manifest: Manifest, gated: Set<string>) {
  const inView = manifest.nodes.filter(
    (n) => n.type === "adoEnvironment" || (n.type === "entryPipeline" && n.category === "deployment"));
  const ids = new Set(inView.map((n) => n.id));

  const nodes: FlowNode[] = inView.map((n) => ({
    id: n.id, data: { label: gatedLabel(n, gated) }, position: { x: 0, y: 0 },
    style: boxStyle({ accent: TYPE_COLOR[n.type], width: 220, fontSize: 13, weight: 600 }),
  }));

  const seen = new Set<string>();
  const edges: FlowEdge[] = [];
  for (const e of manifest.edges) {
    if ((e.kind !== "deploysTo" && e.kind !== "gatedBy") || !ids.has(e.from) || !ids.has(e.to)) continue;
    const key = `${e.kind}:${e.from}>${e.to}`;
    if (seen.has(key)) continue;
    seen.add(key);
    const gate = e.kind === "gatedBy";
    edges.push({
      id: key, source: e.from, target: e.to, label: gate ? "awaiting approval" : "deploys to",
      markerEnd: { type: MarkerType.ArrowClosed, color: gate ? "#b45309" : edgeColor("deploysTo") },
      style: { stroke: gate ? "#b45309" : edgeColor("deploysTo"), strokeWidth: 1.5, strokeDasharray: gate ? "5 4" : undefined },
    });
  }
  return layout(nodes, edges, { nodeWidth: 220, nodeHeight: 46 });
}

// Policy pipelines as a compact grid — most are standalone gates, so a grid reads
// far better (and fits at a consistent zoom) than a tall dagre column. Click for
// details / what it enforces; double-click drills into its steps.
function buildPolicyView(manifest: Manifest, selectedId: string | null, passes: (n: Node) => boolean, stops: Set<string>) {
  const members = manifest.nodes
    .filter((n) => n.type === "entryPipeline" && n.category === "policy")
    .sort((a, b) => displayName(a).localeCompare(displayName(b)));

  const COLS = 4;
  const COL_W = 230;
  const ROW_H = 64;
  const nodes: FlowNode[] = members.map((n, i) => ({
    id: n.id,
    data: { label: gatedLabel(n, stops) },
    position: { x: (i % COLS) * COL_W, y: Math.floor(i / COLS) * ROW_H },
    style: boxStyle({ accent: TYPE_COLOR[n.type], selected: n.id === selectedId, opacity: passes(n) ? 1 : 0.3, fontSize: 12, weight: 600, width: 210 }),
  }));
  return { nodes, edges: [] as FlowEdge[] };
}

// All files, grouped by subsystem into columns — a browsable index (no edges, so
// it stays legible at 100+ nodes). Cluster headers drill into that subsystem.
function buildAllFilesView(
  manifest: Manifest,
  clusters: { id: string; label: string; count: number }[],
  selectedId: string | null,
  passes: (n: Node) => boolean,
  gated: Set<string>,
) {
  const COL_W = 210;
  const NODE_W = 186;
  const GAP = 10; // vertical space between one node's bottom and the next node's top
  const nodes: FlowNode[] = [];

  const byCluster = new Map<string, Node[]>();
  for (const n of manifest.nodes) (byCluster.get(clusterKey(n)) ?? byCluster.set(clusterKey(n), []).get(clusterKey(n))!).push(n);

  clusters.forEach((c, ci) => {
    const x = ci * COL_W;
    const members = (byCluster.get(c.id) ?? []).slice().sort((a, b) => displayName(a).localeCompare(displayName(b)));
    nodes.push({
      id: `cluster:${c.id}`,
      data: { label: `${c.label} (${members.length})`, kind: "cluster", clusterId: c.id },
      position: { x, y: 0 },
      style: { background: "#eef2ff", border: "1px solid #c7d2fe", borderRadius: 8, padding: "6px 10px", fontSize: 12, fontWeight: 700, width: NODE_W },
    });
    // Cumulative Y so long, wrapped names never overlap (heights estimated from label length).
    let y = 44;
    for (const n of members) {
      const label = gatedLabel(n, gated);
      const lines = Math.max(1, Math.ceil(label.length / 26));
      nodes.push({
        id: n.id,
        data: { label },
        position: { x, y },
        style: boxStyle({ accent: TYPE_COLOR[n.type], selected: n.id === selectedId, opacity: passes(n) ? 1 : 0.25, fontSize: 11, width: NODE_W }),
      });
      y += lines * 15 + 16 + GAP; // ~lineHeight*lines + padding + gap
    }
  });

  return { nodes, edges: [] as FlowEdge[] };
}

function buildClusterView(
  manifest: Manifest,
  clusters: { id: string; label: string; count: number }[],
  tagFilter: string,
  byId: Map<string, Node>,
) {
  const active = new Set<string>();
  if (tagFilter) {
    for (const n of manifest.nodes) if (n.tags.some((t) => `${t.kind}:${t.id}` === tagFilter)) active.add(clusterKey(n));
  }
  const nodes: FlowNode[] = clusters.map((c) => ({
    id: `cluster:${c.id}`, data: { label: `${c.label}  (${c.count})`, kind: "cluster", clusterId: c.id }, position: { x: 0, y: 0 },
    style: { background: "#eef2ff", border: "1px solid #c7d2fe", borderRadius: 10, padding: "10px 14px", fontSize: 13, fontWeight: 600, width: 220, opacity: tagFilter && !active.has(c.id) ? 0.3 : 1 },
  }));
  const structural = new Set(["includesTemplate", "extends", "deploysTo", "runsOnEnvironment", "gatedBy", "callsScript"]);
  const seen = new Set<string>();
  const edges: FlowEdge[] = [];
  for (const e of manifest.edges) {
    if (!structural.has(e.kind)) continue;
    const a = clusterKey(byId.get(e.from) ?? ({} as Node));
    const b = clusterKey(byId.get(e.to) ?? ({} as Node));
    if (a === b) continue;
    const key = `${a}>${b}`;
    if (seen.has(key)) continue;
    seen.add(key);
    edges.push({ id: key, source: `cluster:${a}`, target: `cluster:${b}`, markerEnd: { type: MarkerType.ArrowClosed }, style: { stroke: "#94a3b8" } });
  }
  return layout(nodes, edges, { nodeWidth: 220, nodeHeight: 46 });
}

function buildFilesView(manifest: Manifest, cluster: string | null, selectedId: string | null, passes: (n: Node) => boolean, gated: Set<string>) {
  const members = new Set(manifest.nodes.filter((n) => cluster === null || clusterKey(n) === cluster).map((n) => n.id));
  const visible = new Set(members);
  if (cluster !== null) for (const e of manifest.edges) { if (members.has(e.from)) visible.add(e.to); if (members.has(e.to)) visible.add(e.from); }

  const nodes: FlowNode[] = [];
  for (const n of manifest.nodes) {
    if (!visible.has(n.id)) continue;
    const dimmed = !members.has(n.id) || !passes(n);
    const s = statusStyle(n.status);
    nodes.push({ id: n.id, data: { label: gatedLabel(n, gated) }, position: { x: 0, y: 0 },
      style: boxStyle({ accent: TYPE_COLOR[n.type], selected: n.id === selectedId, dashed: s.dashed, opacity: dimmed ? 0.2 : s.opacity }) });
  }
  const edges: FlowEdge[] = manifest.edges.filter((e) => visible.has(e.from) && visible.has(e.to))
    .map((e) => ({ id: e.id, source: e.from, target: e.to, markerEnd: { type: MarkerType.ArrowClosed, color: edgeColor(e.kind) }, style: { stroke: edgeColor(e.kind), strokeWidth: 1.5 } }));
  return layout(nodes, edges, { nodeWidth: 190, nodeHeight: 48 });
}

function buildStepsView(steps: Step[], selectedStepId: string | null) {
  const ids = new Set(steps.map((s) => s.id));
  const nodes: FlowNode[] = steps.map((s) => ({
    id: s.id, data: { label: (s.manualPause ? `${GATE_ICON} ` : "") + s.name }, position: { x: 0, y: 0 },
    style: boxStyle({ accent: STEP_KIND_COLOR[s.kind], selected: s.id === selectedStepId, width: 230 }),
  }));
  const edges: FlowEdge[] = [];
  const lastByParent = new Map<string, string>();
  for (const s of steps) {
    const parent = s.parentId ?? "";
    const from = lastByParent.get(parent) ?? (s.parentId && ids.has(s.parentId) ? s.parentId : null);
    if (from) edges.push({ id: `${from}>${s.id}`, source: from, target: s.id, markerEnd: { type: MarkerType.ArrowClosed }, style: { stroke: "#cbd5e1" } });
    lastByParent.set(parent, s.id);
  }
  return layout(nodes, edges, { direction: "TB", nodeWidth: 230, nodeHeight: 42 });
}
