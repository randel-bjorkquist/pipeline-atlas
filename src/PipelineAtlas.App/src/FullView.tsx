import { useEffect } from "react";
import { marked } from "marked";
import hlCss from "highlight.js/styles/github.css?inline";
import { highlight } from "./highlight";

export type FullContent =
  | { kind: "markdown"; title: string; text: string }
  | { kind: "code"; title: string; text: string; path: string };

function escapeHtml(s: string): string {
  return s.replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;");
}

// A comfortable full-screen reader for a README (rendered) or a source file
// (syntax-highlighted). Esc / [X] / clicking the backdrop closes it; "Open in new
// tab" builds a standalone highlighted HTML page as a blob (no server needed).
export function FullView({ content, onClose }: { content: FullContent; onClose: () => void }) {
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => { if (e.key === "Escape") onClose(); };
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [onClose]);

  const openNewTab = () => {
    const inner = content.kind === "markdown"
      ? `<div class="md">${marked.parse(content.text, { async: false }) as string}</div>`
      : `<pre class="hljs"><code>${highlight(content.text, content.path)}</code></pre>`;
    const doc =
      `<!doctype html><html><head><meta charset="utf-8"><title>${escapeHtml(content.title)}</title>` +
      `<style>${hlCss}body{font-family:ui-sans-serif,system-ui,sans-serif;margin:24px;color:#0f172a}` +
      `pre{padding:16px;border-radius:8px;overflow:auto;background:#f6f8fa}code{font-size:13px}` +
      `.md{max-width:820px;line-height:1.6}</style></head><body><h3>${escapeHtml(content.title)}</h3>${inner}</body></html>`;
    window.open(URL.createObjectURL(new Blob([doc], { type: "text/html" })), "_blank");
  };

  return (
    <div className="modal-backdrop" onClick={onClose}>
      <div className="modal" onClick={(e) => e.stopPropagation()}>
        <div className="modal-head">
          <span className="modal-title">{content.title}</span>
          <span className="spacer" />
          <button className="btn" onClick={openNewTab}>Open in new tab ↗</button>
          <button className="modal-x" onClick={onClose} aria-label="Close">✕</button>
        </div>
        <div className="modal-body">
          {content.kind === "markdown"
            ? <div className="markdown" dangerouslySetInnerHTML={{ __html: marked.parse(content.text, { async: false }) as string }} />
            : <pre className="source hljs source-full"><code dangerouslySetInnerHTML={{ __html: highlight(content.text, content.path) }} /></pre>}
        </div>
      </div>
    </div>
  );
}
