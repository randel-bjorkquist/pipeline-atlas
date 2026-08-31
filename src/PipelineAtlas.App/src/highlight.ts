import hljs from "highlight.js/lib/core";
import yaml from "highlight.js/lib/languages/yaml";
import powershell from "highlight.js/lib/languages/powershell";
import json from "highlight.js/lib/languages/json";
import bash from "highlight.js/lib/languages/bash";
import xml from "highlight.js/lib/languages/xml";
import "highlight.js/styles/github.css";

hljs.registerLanguage("yaml", yaml);
hljs.registerLanguage("powershell", powershell);
hljs.registerLanguage("json", json);
hljs.registerLanguage("bash", bash);
hljs.registerLanguage("xml", xml);

// The bundled default highlighter (a downloadable/configurable one is a later phase).
export function langFor(path: string): string | null {
  const p = path.toLowerCase();
  if (p.endsWith(".yml") || p.endsWith(".yaml")) return "yaml";
  if (p.endsWith(".ps1") || p.endsWith(".psm1")) return "powershell";
  if (p.endsWith(".json")) return "json";
  if (p.endsWith(".sh")) return "bash";
  if (p.endsWith(".xml") || p.endsWith(".config")) return "xml";
  return null;
}

function escapeHtml(s: string): string {
  return s.replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;");
}

/** Returns highlighted HTML for the file's content, or escaped text if unknown. */
export function highlight(text: string, path: string): string {
  const lang = langFor(path);
  if (lang) {
    try {
      return hljs.highlight(text, { language: lang }).value;
    } catch {
      /* fall through to plain */
    }
  }
  return escapeHtml(text);
}
