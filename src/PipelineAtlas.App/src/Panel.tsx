import { useEffect, useState } from "react";
import type { FullContent } from "./FullView";
import { highlight } from "./highlight";

// Shared building blocks for the right-hand panel so nodes and steps render the
// same accordion (DOCUMENTATION / DETAILS / SOURCE).

export function Section({ title, defaultOpen, children }: { title: string; defaultOpen?: boolean; children: React.ReactNode }) {
  const [open, setOpen] = useState(defaultOpen ?? false);
  return (
    <div className="acc">
      <button className="acc-head" onClick={() => setOpen((o) => !o)}>
        <span className="acc-caret">{open ? "▾" : "▸"}</span> {title}
      </button>
      {open && <div className="acc-body">{children}</div>}
    </div>
  );
}

// The raw file, served live by `patlas view` (and the dev middleware) at /source.
// `onOpenFull` pops the highlighted full view; the header shows the file name.
export function SourceView({ path, openFull }: { path: string; openFull?: (c: FullContent) => void }) {
  const [text, setText] = useState<string | null>(null);
  const [err, setErr] = useState(false);
  useEffect(() => {
    let live = true;
    setText(null);
    setErr(false);
    fetch(`./source?path=${encodeURIComponent(path)}`)
      .then((r) => (r.ok ? r.text() : Promise.reject(new Error(String(r.status)))))
      .then((t) => live && setText(t))
      .catch(() => live && setErr(true));
    return () => { live = false; };
  }, [path]);

  const name = path.split("/").pop() ?? path;
  if (err) return <div className="muted">Source not available here.</div>;
  if (text === null) return <div className="muted">Loading…</div>;
  const onFull = () =>
    openFull
      ? openFull({ kind: "code", title: name, text, path })
      : window.open(`./source?path=${encodeURIComponent(path)}`, "_blank");
  return (
    <>
      <button className="filelink" onClick={onFull}>
        {name}<span className="menu-hint">full view</span>
      </button>
      <pre className="source hljs"><code dangerouslySetInnerHTML={{ __html: highlight(text, path) }} /></pre>
    </>
  );
}
