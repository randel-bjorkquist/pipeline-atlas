import { useEffect, useRef } from "react";
import type { FullContent } from "./FullView";
import { displayName, type Node } from "./manifest";
import { Markdown } from "./Markdown";
import { Section, SourceView, WorkItems } from "./Panel";
import { STATUS_LABEL, TYPE_COLOR, TYPE_LABEL } from "./theme";

export interface RelatedDoc {
  title: string;
  body: string;
}

export function DocPanel({
  node,
  stepCount,
  onOpenSteps,
  facts = [],
  docs = [],
  extra,
  sourcePath,
  openFull,
}: {
  node: Node | null;
  stepCount?: number;
  onOpenSteps?: () => void;
  facts?: string[];
  docs?: RelatedDoc[];
  extra?: React.ReactNode; // node-type-specific content (e.g. environment info)
  sourcePath?: string; // file to show under SOURCE (defaults to node.path)
  openFull?: (c: FullContent) => void;
}) {
  const ref = useRef<HTMLElement>(null);
  useEffect(() => {
    if (ref.current) ref.current.scrollTop = 0; // scroll to top when the node changes
  }, [node?.id]);

  if (!node) {
    return (
      <aside className="doc empty" ref={ref}>
        Select a node to see its documentation, details and source.
      </aside>
    );
  }

  return (
    <aside className="doc" ref={ref}>
      <h2>{displayName(node)}</h2>
      {node.label && node.title !== node.label && <div className="sub">{node.title}</div>}
      <div className="sub">
        <span style={{ color: TYPE_COLOR[node.type], fontWeight: 600 }}>{TYPE_LABEL[node.type]}</span>
        {" · "}{STATUS_LABEL[node.status]}
        {node.category && <> · <span style={{ textTransform: "capitalize" }}>{node.category}</span></>}
      </div>
      <WorkItems tags={node.tags} />

      {onOpenSteps && stepCount ? (
        <button className="btn" style={{ margin: "6px 0 10px" }} onClick={onOpenSteps}>Open steps ({stepCount}) →</button>
      ) : null}

      <div key={node.id}>
        <Section title="DOCUMENTATION" defaultOpen>
          {docs.length > 0 ? docs.map((d) => <Markdown key={d.title} title={d.title} body={d.body} openFull={openFull} />)
            : <div className="muted">No README found for this item.</div>}
        </Section>

        <Section title="DETAILS">
          {extra}
          {facts.length > 0 && (
            <>
              <div className="section-title">What it does</div>
              <ul className="facts">{facts.map((f, i) => <li key={i}>{f}</li>)}</ul>
            </>
          )}
          {node.purpose && (
            <>
              <div className="section-title">Purpose</div>
              <p className="purpose">{node.purpose}</p>
            </>
          )}
          {node.path && (
            <div className="row">
              <span className="k">Path</span>
              <a href={`./source?path=${encodeURIComponent(node.path)}`} target="_blank" rel="noreferrer">{node.path} ↗</a>
            </div>
          )}
          {node.trigger && <div className="row"><span className="k">Trigger</span><span>{node.trigger}</span></div>}
          {node.pool && <div className="row"><span className="k">Pool</span><span>{node.pool}</span></div>}
          {node.source && <div className="row"><span className="k">Source</span><span>{node.source}</span></div>}

          {node.parameters && node.parameters.length > 0 && (
            <>
              <div className="section-title">Parameters</div>
              {node.parameters.map((p) => (
                <div className="param" key={p.name}>
                  <div><span className="pname">{p.name}</span>{p.type && <span className="ptype">: {p.type}</span>}
                    {p.default !== undefined && <span className="pdefault"> = {p.default || "″″"}</span>}</div>
                  {p.description && <div className="pdesc">{p.description}</div>}
                </div>
              ))}
            </>
          )}

          {node.flags.length > 0 && (
            <>
              <div className="section-title">Flags</div>
              {node.flags.map((f, i) => <div className="flag" key={i}><span className="sev">{f.severity}</span> — {f.note}</div>)}
            </>
          )}
        </Section>

        {(sourcePath ?? node.path) && (
          <Section title="SOURCE">
            <SourceView path={(sourcePath ?? node.path)!} openFull={openFull} />
          </Section>
        )}
      </div>
    </aside>
  );
}
