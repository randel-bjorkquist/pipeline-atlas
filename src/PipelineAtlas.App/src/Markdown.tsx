import { useMemo, useState } from "react";
import { marked } from "marked";
import type { FullContent } from "./FullView";

// Renders a node's README/markdown as clean formatted text — end users never see
// raw Markdown syntax (T4b). Long docs collapse behind a toggle; "full view" opens
// the reader modal.
export function Markdown({ title, body, openFull }: { title: string; body: string; openFull?: (c: FullContent) => void }) {
  const [open, setOpen] = useState(false);
  const html = useMemo(() => marked.parse(body, { async: false }) as string, [body]);

  return (
    <div className="mdblock">
      <button className="mdtoggle" onClick={() => setOpen((o) => !o)}>
        {open ? "▾" : "▸"} {title}
      </button>
      {openFull && (
        <button className="filelink" style={{ marginLeft: 10 }} onClick={() => openFull({ kind: "markdown", title, text: body })}>
          full view
        </button>
      )}
      {open && <div className="markdown" dangerouslySetInnerHTML={{ __html: html }} />}
    </div>
  );
}
