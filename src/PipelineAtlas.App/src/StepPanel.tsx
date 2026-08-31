import { useEffect, useRef } from "react";
import type { FullContent } from "./FullView";
import type { Step } from "./manifest";
import type { RelatedDoc } from "./DocPanel";
import { Markdown } from "./Markdown";
import { Section, SourceView } from "./Panel";
import { STEP_KIND_COLOR, STEP_KIND_LABEL } from "./theme";

// The 100-ft counterpart to DocPanel — same accordion (Documentation / Details /
// Source) so a step reads consistently: what it does, plus the file it lives in.
export function StepPanel({ step, sourcePath, docs = [], openFull }: { step: Step | null; sourcePath?: string; docs?: RelatedDoc[]; openFull?: (c: FullContent) => void }) {
  const ref = useRef<HTMLElement>(null);
  useEffect(() => { if (ref.current) ref.current.scrollTop = 0; }, [step?.id]);

  if (!step) {
    return <aside className="doc empty" ref={ref}>Select a step to see its details and source.</aside>;
  }

  return (
    <aside className="doc" ref={ref}>
      <h2>{step.name}</h2>
      <div className="sub"><span style={{ color: STEP_KIND_COLOR[step.kind], fontWeight: 600 }}>{STEP_KIND_LABEL[step.kind]}</span></div>

      <div key={step.id}>
        <Section title="DOCUMENTATION" defaultOpen>
          {docs.length > 0 ? docs.map((d) => <Markdown key={d.title} title={d.title} body={d.body} openFull={openFull} />)
            : <div className="muted">No README for this file.</div>}
        </Section>

        <Section title="DETAILS">
          {step.doc && (
            <>
              <div className="section-title">What it does</div>
              <p className="purpose">{step.doc}</p>
            </>
          )}
          {step.action && <div className="row"><span className="k">Action</span><span>{step.action}</span></div>}
          <div className="row"><span className="k">Kind</span><span>{STEP_KIND_LABEL[step.kind]}</span></div>
          {step.externalDeps && step.externalDeps.length > 0 && (
            <>
              <div className="section-title">External deps</div>
              <div className="chips">{step.externalDeps.map((d) => <span className="chip" key={d}>{d}</span>)}</div>
            </>
          )}
          {step.tags.length > 0 && (
            <>
              <div className="section-title">Work items</div>
              <div className="chips">
                {step.tags.map((t) => t.url
                  ? <a key={`${t.kind}:${t.id}`} className="chip" href={t.url} target="_blank" rel="noreferrer">{t.kind} {t.id}</a>
                  : <span key={`${t.kind}:${t.id}`} className="chip">{t.kind} {t.id}</span>)}
              </div>
            </>
          )}
        </Section>

        {sourcePath && (
          <Section title="SOURCE">
            <SourceView path={sourcePath} openFull={openFull} />
          </Section>
        )}
      </div>
    </aside>
  );
}
