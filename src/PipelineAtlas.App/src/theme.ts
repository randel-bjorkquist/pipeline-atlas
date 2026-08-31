// Visual vocabulary: one colour per node type, plus status treatments. Kept in
// one place so the graph, legend and doc panel stay consistent.

import type { CSSProperties } from "react";
import type { EdgeKind, NodeStatus, NodeType, StepKind } from "./manifest";

// Node box style built from longhand border props only (mixing the `border`
// shorthand with `borderLeft` triggers React warnings on rerender).
export function boxStyle(opts: {
  accent: string;
  selected?: boolean;
  dashed?: boolean;
  opacity?: number;
  width?: number;
  fontSize?: number;
  weight?: number;
}): CSSProperties {
  return {
    background: "#ffffff",
    borderStyle: opts.dashed ? "dashed" : "solid",
    borderWidth: "1px",
    borderColor: opts.selected ? "#2563eb" : "#cbd5e1",
    borderLeftStyle: "solid",
    borderLeftWidth: "5px",
    borderLeftColor: opts.accent,
    borderRadius: 8,
    padding: "6px 10px",
    fontSize: opts.fontSize ?? 12,
    fontWeight: opts.weight,
    width: opts.width ?? 190,
    opacity: opts.opacity ?? 1,
    boxShadow: opts.selected ? "0 0 0 2px #bfdbfe" : "0 1px 2px rgba(0,0,0,0.06)",
  };
}

export const STEP_KIND_COLOR: Record<StepKind, string> = {
  stage: "#2563eb", // blue
  job: "#0d9488", // teal
  step: "#64748b", // slate
  templateInclude: "#7c3aed", // violet
};

export const STEP_KIND_LABEL: Record<StepKind, string> = {
  stage: "Stage",
  job: "Job",
  step: "Step",
  templateInclude: "Template include",
};

export const TYPE_COLOR: Record<NodeType, string> = {
  entryPipeline: "#2563eb", // blue
  template: "#0d9488", // teal
  envConfig: "#d97706", // amber
  psModule: "#7c3aed", // violet
  psScript: "#9333ea", // purple
  test: "#16a34a", // green
  doc: "#64748b", // slate
  data: "#475569", // dark slate
  adoEnvironment: "#db2777", // pink
  adoResource: "#0891b2", // cyan
  external: "#94a3b8", // grey
};

export const TYPE_LABEL: Record<NodeType, string> = {
  entryPipeline: "Entry pipeline",
  template: "Template",
  envConfig: "Env config",
  psModule: "PS module",
  psScript: "PS script",
  test: "Test",
  doc: "Doc",
  data: "Data",
  adoEnvironment: "Environment",
  adoResource: "Resource",
  external: "External",
};

export const STATUS_LABEL: Record<NodeStatus, string> = {
  active: "Active",
  "legacy-active": "Legacy (active)",
  dormant: "Dormant",
  archived: "Archived",
};

/** How a node's status dims/marks it. */
export function statusStyle(status: NodeStatus): {
  opacity: number;
  dashed: boolean;
} {
  switch (status) {
    case "archived":
      return { opacity: 0.45, dashed: true };
    case "dormant":
      return { opacity: 0.6, dashed: true };
    case "legacy-active":
      return { opacity: 0.85, dashed: false };
    default:
      return { opacity: 1, dashed: false };
  }
}

export const EDGE_COLOR: Partial<Record<EdgeKind, string>> = {
  includesTemplate: "#0d9488",
  extends: "#2563eb",
  runsOnEnvironment: "#db2777",
  deploysTo: "#db2777",
  callsScript: "#7c3aed",
  testedBy: "#16a34a",
};

export function edgeColor(kind: EdgeKind): string {
  return EDGE_COLOR[kind] ?? "#94a3b8";
}
