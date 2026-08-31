# Pipeline Atlas — Tool Charter

> This is the project memory for Claude Code. Open this repo in the Code tab, start in **Plan** mode,
> and say: *"Read CLAUDE.md and propose a Phase 1 plan and repo scaffolding."* Review the plan, approve,
> then switch to **Accept edits** to build. Everything below describes a **generic** tool — no project,
> repo, or product name belongs anywhere in `packages/`. Project-specific data lives only in a target
> folder's own files (see §4 and §12).

---

## 1. What this is

**Pipeline Atlas turns a folder of Azure DevOps pipeline files into an interactive, zoomable map.** Point
it at any directory of pipeline YAML, templates, environment configs, and PowerShell, and it renders how
they connect — system → subsystem → file → step, and back out again — with plain-language docs and
work-item tags at every level.

Three properties define it:

- **Folder in, map out.** The input is a *path you pass in*, never a fixed location. `patlas view ./some/folder`
  is the same operation whether that folder is inside this repo, beside it, or on another machine.
- **Project-agnostic.** No repository name, path, or work-item URL is baked into the engine or app. The
  folder being analyzed **describes itself** through a small `.patlas.json` (see §4).
- **The map is a projection of the source.** A generator parses the folder into one `manifest.json`; the
  app renders only that manifest. Regenerate, and the picture updates. The app never reads the source repo
  or Azure DevOps at runtime.

```
target folder (YAML, templates, env, PowerShell, docs)  +  .patlas.json
        │
        ▼   core engine  (deterministic: folder path → manifest.json)
   manifest.json  (validated, versionable)
        │
        ▼   app  (static React site; reads the manifest, nothing else)
   interactive map  (zoom / click-to-doc / tag filter / animated run)
```

---

## 2. Architecture & portability

A .NET solution with three projects, so the engine that makes it portable is cleanly separable
from the UI:

- **`core`** — the whole point of portability. A pure function: *given a folder path (+ its `.patlas.json`),
  return a validated `manifest.json`.* No UI, no project coupling, no assumption beyond "here is a directory."
- **`cli`** — what you install and run anywhere:
  - `patlas analyze <folder> -o manifest.json` — scan and build the manifest.
  - `patlas init <folder>` — drop a starter `.patlas.json` into a new target.
  - `patlas view <folder>` — analyze *and* open the app on that manifest (the everyday command).
- **`app`** — React viewer that loads a manifest and renders the map. Separate so it can build to a static
  site, be embedded by the CLI, or be hosted on its own.

**Export path:** `dotnet publish` the CLI as a self-contained, single-file executable, then on any machine
`patlas view /path/to/a/pipeline/folder` — no .NET (or Node) install required on that machine. The viewer
builds to static assets embedded in the exe, so there is no server to carry.

Start collapsed if it's simpler — the three-package split is the target shape, not a Phase-1 requirement.
Do not over-engineer the scaffolding before one flow parses end to end.

---

## 3. Repo layout (to scaffold)

```
pipeline-atlas/
  PipelineAtlas.slnx           # .NET solution
  Directory.Build.props        # shared build settings (net10.0, nullable, deterministic)
  README.md                    # already present — do not clobber
  LICENSE                      # MIT, already present — do not clobber
  CLAUDE.md                    # this file
  src/
    PipelineAtlas.Core/        # the engine (class library)
      Scanning/                # file discovery (globs from .patlas.json)
      Parsing/                 # Azure Pipelines YAML → structure (YamlDotNet)
      Building/                # assemble + validate nodes/edges/steps/flows/tags/flags
      Model/                   # Node, Edge, Step, Flow, Tag, Flag, Manifest
      Schema/manifest.schema.json   # embedded; validated on every run
    PipelineAtlas.Cli/         # console app: analyze | init | view (publishes to patlas.exe)
    PipelineAtlas.App/         # web viewer (React + Vite; static assets embedded in the exe)
  tests/
    PipelineAtlas.Core.Tests/  # xUnit; golden-file test over fixtures/sample
  fixtures/
    sample/                    # a tiny synthetic pipeline folder for tests + demo
  inputs/                      # co-located real targets
    pha-web/                   # the first target — carries its own .patlas.json + seed.md
```

---

## 4. The target contract — `.patlas.json`

Every folder Pipeline Atlas analyzes carries a `.patlas.json` at its root. This is how a target
self-describes so the engine stays generic. Paths/globs inside it are **relative to the target folder**.

```jsonc
{
  "displayName": "Human-readable name shown in the app",
  "description": "One line about this target.",
  "scan": {
    "include": ["**/*.yml", "**/*.yaml", "**/*.psm1", "**/*.ps1", "**/*.md", "**/*.json"],
    "exclude": ["**/node_modules/**"]
  },
  "workItems": {
    "baseUrl": "https://dev.azure.com/<org>/<project>/_workitems/edit/",
    "tagPatterns": { "story": "story\\s+(\\d+)", "feature": "feature\\s+(\\d+)", "bug": "bug\\s+(\\d+)" }
  },
  "clusters": [
    { "id": "example", "label": "Example subsystem", "match": ["**/foo.yml", "**/Bar.psm1"] }
  ],
  "nodeStatus": [
    { "match": ["**/.archive/**"], "status": "archived" }
  ],
  "archive": { "match": ["**/.archive/**"], "inventoryOnly": true }
}
```

Rules:
- **`scan`** bounds what's parsed. Defaults above are sensible; a target can narrow them.
- **`workItems`** turns comment mentions into clickable links (`baseUrl` + the captured id). `tagPatterns`
  are regexes run over comments; the first capture group is the id.
- **`clusters`** are how *this* target carves its files into subsystems — assigned by first-matching glob.
  If absent, the engine falls back to grouping by top-level folder, then by node type.
- **`nodeStatus`** overrides the default `"active"` status for matched files (e.g. `archived`, `dormant`,
  `legacy-active`).
- **`archive.inventoryOnly`** means: create nodes for matched files so they appear on the map, but skip
  step-level extraction for them.

`patlas init` writes a starter `.patlas.json` with the defaults so a new target is one edit away.

**Config location.** By default the config sits at the target folder's root. Because a target is a
read-only input (§8.1), the config may also live **outside** the target: `patlas analyze|view <folder>
--config <path>` reads it from anywhere (and `patlas init <folder> --config <path>` writes the starter
there). Globs stay relative to the target folder regardless of where the config file lives. This lets a
source-controlled target stay untouched, with its description kept locally (e.g. under git-ignored
`inputs/`).

---

## 5. Manifest schema (the contract everything renders from)

Emit `manifest.json` and validate it against `packages/core/schema/manifest.schema.json` on every run.

```ts
type NodeType =
  | "entryPipeline"    // runnable pipeline (has a trigger / runs directly)
  | "template"         // consumed via template: or extends
  | "envConfig"        // per-environment variables file
  | "psModule"         // *.psm1
  | "psScript"         // *.ps1
  | "test"             // *.Tests.ps1
  | "doc"              // *.md
  | "data"             // *.json etc.
  | "adoEnvironment"   // an ADO Environment (dev/qa/…) — a service object, inferred, not a file
  | "adoResource"      // variable group, agent pool, approval/check — inferred
  | "external";        // referenced but outside the scanned folder

interface Node {
  id: string;               // stable slug, e.g. "pipeline:DevCI"
  type: NodeType;
  path?: string;            // target-relative path for file nodes
  title: string;
  purpose: string;          // plain-language; seed from the file's header comment
  clusterId?: string;       // from .patlas.json
  trigger?: string;         // pipelines: "CI: <branch>" | "manual" | "schedule" | "PR gate"
  pool?: string;
  tags: Tag[];
  flags: Flag[];
  status: "active" | "legacy-active" | "dormant" | "archived";
  source?: "parsed" | "inferred";   // inferred = ADO service node, not a file
}

type EdgeKind =
  | "extends" | "includesTemplate" | "callsScript" | "runsOnEnvironment"
  | "deploysTo" | "consumesArtifact" | "producesArtifact" | "gatedBy"
  | "testedBy" | "referencesExternal" | "documents";

interface Edge { id: string; from: string; to: string; kind: EdgeKind; atStepId?: string; tags?: Tag[]; }

interface Step {                 // the ordered internals of a pipeline/template
  id: string;                    // "DevCI/Deploy_Dev/apply-config"
  nodeId: string;
  parentId?: string;             // stage → job → step nesting
  kind: "stage" | "job" | "step" | "templateInclude";
  name: string;
  doc: string;                   // seed from inline comments
  action?: string;               // task/command summary
  externalDeps?: string[];       // NuGet, VSBuild, sqlpackage, sqlcmd, UNC shares, DBs, .sln/.slnf
  tags: Tag[];
}

interface Flow {                 // an ordered walk that powers the animated run
  id: string; title: string; trigger: string;
  path: string[];                // ordered node/step ids traversed, across environments
  notes?: string;
}

interface Tag  { kind: "story" | "feature" | "bug"; id: string; url?: string; }
interface Flag { severity: "secret" | "hardcode" | "techdebt" | "duplication" | "antipattern"; note: string; stepId?: string; }

interface Manifest {
  target: { displayName: string; description?: string; generatedAt: string; toolVersion: string; };
  nodes: Node[]; edges: Edge[]; steps: Step[]; flows: Flow[];
}
```

---

## 6. Parsing rules (Azure Pipelines YAML + PowerShell)

- `extends:` → **extends** edge to the referenced template.
- `- template: <path>` → **includesTemplate**. Template paths inside YAML are real relative paths — resolve
  them against the target folder.
- PowerShell references — `filePath: '…/X.ps1'`, `Import-Module …/X.psm1`, `scripts\X.psm1` — → **callsScript**.
- Deployment jobs with `environment:` → **runsOnEnvironment** to a (possibly inferred) `adoEnvironment` node.
- A deploy template's target-tier parameter (e.g. `stage: dev|qa|…`) → **deploysTo**.
- Artifact publish/download steps → **producesArtifact** / **consumesArtifact**.
- **gatedBy** edges can't be seen in files; synthesize them from the target's governance module/doc
  (a target can point at these in `.patlas.json` later) and mark the nodes `source: "inferred"`.
- Module ↔ `*.Tests.ps1` by name → **testedBy**.
- Comments matching `workItems.tagPatterns` → **Tags** on the node, and on the Step when the comment sits
  on a step. Build `url` from `workItems.baseUrl` + id.
- **Flows** are auto-derived: one per `entryPipeline`, its `path` = the ordered walk through its stages →
  jobs → steps and included templates, resolving `deploysTo`/`runsOnEnvironment`.

---

## 7. Interaction model (the altitude ladder)

Four zoom levels, matching how the system is meant to be read:

1. **30,000 ft — system:** the handful of flows and the environments they cross. One screen.
2. **10,000 ft — clusters:** subsystems as groups; edges between them.
3. **1,000 ft — files:** individual nodes; click for purpose, trigger, tags, flags.
4. **100 ft — steps:** inside a pipeline — stage → job → step, each with its doc, external deps, tags.

At every level: **click → doc panel** (purpose + tags as clickable work-item links + flags), and **filter
by tag** (show only what a given story touched). **Phase 2:** *play a flow* — animate `Flow.path` across
the environments to watch a promotion move.

---

## 8. Guardrails

1. **The target folder is a read-only input.** The engine parses it; it must never modify a file in a
   target. All tool code stays under `packages/` (and `fixtures/`).
2. **Never analyze yourself.** Always scan the folder passed in, never an implicit repo root. The CLI
   should refuse (or warn) if the target looks like the Pipeline Atlas repo (e.g. contains `packages/core`).
3. **Deterministic output** — stable ordering and formatting, so `manifest.json` diffs are meaningful.
4. **The app reads only `manifest.json`.** No live repo or Azure DevOps access at runtime.
5. **No secrets, ever, in the manifest or app.** If a value looks secret-like, record a `Flag`, not the value.
6. **Docs come from the files first.** Seed each node/step's prose from its header/inline comments, and
   allow a markdown override so hand-authored copy isn't overwritten on regenerate.
7. When building the app UI, follow good visual-design practice (there is a `frontend-design` skill for this).

---

## 9. Tech defaults (resolved — see §10)

- **`core` + `cli`: C# on .NET 10.** `YamlDotNet` for pipelines; a light tokenizer/regex pass for PowerShell
  exports and references; `JsonSchema.Net` to validate the manifest. The CLI publishes as a **self-contained,
  single-file executable** so an end user needs neither .NET nor Node installed.
- **`app`: a web viewer.** JavaScript is acceptable in this layer: the map wants the richest zoomable-graph
  tooling (**React Flow (`@xyflow/react`)** with `elkjs`/`dagre` layout). It builds to static assets that ship
  *inside* the executable, so the end user just runs the exe and the browser opens. (Node is a build-time
  dependency for the developer only.)

---

## 10. Decisions (resolved)

- **`core`/`cli` language:** **C# on .NET 10.** (An earlier TypeScript prototype is parked on the
  `phase1-core` branch.) The owner wants readable, ownable C# and a self-contained executable.
- **Graph library:** **React Flow** for the web viewer; JavaScript is acceptable in the viewer layer only.
- **ADO enrichment:** infer environments/gates from files now; live ADO REST approval/gate state is a later
  phase. Work-item references are **best-effort** — failures degrade to Information-level messages, never fatal.
- **`inputs/` in git:** **git-ignored** — real analysis targets are kept locally and are *not* committed to the repo.

---

## 11. Phase 1 tasks

1. Scaffold the workspace (`packages/core|cli|app`, `fixtures/sample/`, empty `manifest.schema.json`).
2. `core`: parse **one flow end-to-end** against `fixtures/sample/` (an entry pipeline → an included build
   template → a deploy template with an environment). Emit and validate `manifest.json`.
3. Wire the `cli`: `analyze`, then `view` (serve the app on the generated manifest).
4. `app`: render the four altitude levels + click-to-doc + tag filter. (Animated flow run is Phase 2.)
5. Point it at the real target: `patlas view ./inputs/pha-web` (see §12) and iterate on true data.
6. Add `parse-powershell` (nodes, `callsScript`, `testedBy`), then inferred `adoEnvironment`/`gatedBy` nodes.
7. Add an `npm run generate` script and document the export flow in `README.md`.

---

## 12. Working target for this checkout — *local only; not committed*

The first real target lives at **`inputs/pha-web/`** on the local machine and carries its own
`.patlas.json` and a `seed.md`. **`inputs/` is git-ignored** — real targets are analyzed locally and are not
part of the repo (the committed example is `fixtures/sample/`). The seed is human context the parser can't
infer (which pipelines form the promotion spine, how the files cluster into subsystems, which are
legacy/dormant/archived, and which work-items to expect); treat the **generator** as the source of truth —
the seed describes the *expected* shape, not the authoritative one. Run against it locally with
`patlas view ./inputs/pha-web`. Before linking tags, set the real `workItems.baseUrl` in that target's
`.patlas.json`.
