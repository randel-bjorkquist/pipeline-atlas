// Deterministic graph layout via dagre. React Flow gives pan/zoom + rendering;
// dagre positions the nodes so the map reads left-to-right (sources -> targets).

import Dagre from "@dagrejs/dagre";
import type { Edge as FlowEdge, Node as FlowNode } from "@xyflow/react";

export interface LaidOut {
  nodes: FlowNode[];
  edges: FlowEdge[];
}

export function layout(
  nodes: FlowNode[],
  edges: FlowEdge[],
  options: { direction?: "LR" | "TB"; nodeWidth?: number; nodeHeight?: number } = {},
): LaidOut {
  const direction = options.direction ?? "LR";
  const nodeWidth = options.nodeWidth ?? 190;
  const nodeHeight = options.nodeHeight ?? 52;

  const g = new Dagre.graphlib.Graph().setDefaultEdgeLabel(() => ({}));
  g.setGraph({ rankdir: direction, nodesep: 24, ranksep: 90, marginx: 20, marginy: 20 });

  for (const node of nodes) {
    g.setNode(node.id, { width: nodeWidth, height: nodeHeight });
  }
  for (const edge of edges) {
    // Only lay out edges whose endpoints are both present at this level.
    if (g.hasNode(edge.source) && g.hasNode(edge.target)) {
      g.setEdge(edge.source, edge.target);
    }
  }

  Dagre.layout(g);

  const laidOutNodes = nodes.map((node) => {
    const pos = g.node(node.id);
    return {
      ...node,
      position: { x: pos.x - nodeWidth / 2, y: pos.y - nodeHeight / 2 },
      // Left-to-right handles so edges attach on the sides.
      sourcePosition: direction === "LR" ? "right" : "bottom",
      targetPosition: direction === "LR" ? "left" : "top",
    } as FlowNode;
  });

  return { nodes: laidOutNodes, edges };
}
