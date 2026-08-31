# pipeline-atlas

Pipeline Atlas turns a folder of Azure DevOps pipeline YAML and PowerShell into an interactive, zoomable map. Point it at any pipeline folder and navigate the system from a 30,000-ft overview down to individual steps, with plain-language docs and work-item tags at every level. Folder in, map out.

## What it is

- **Folder in, map out.** The input is a path you pass in, never a fixed location.
- **Project-agnostic.** Nothing about a specific repo is baked into the tool; a target folder describes itself through a small `.patlas.json`.
- **The map is a projection.** The engine parses the folder into one `manifest.json`; the viewer renders only that manifest. Regenerate, and the picture updates.

## Layout

- `src/PipelineAtlas.Core` — the engine (C#/.NET 10 class library): a folder path (+ `.patlas.json`) → a validated `manifest.json`.
- `src/PipelineAtlas.Cli` — the `patlas` console app: `analyze` / `init` / `view`.
- `src/PipelineAtlas.App` — the viewer (React + Vite + React Flow); builds to static assets embedded in the exe.
- `tests/PipelineAtlas.Core.Tests` — xUnit golden-file tests over `fixtures/sample`.

## Commands

```
patlas analyze <folder> [-o manifest.json]   scan a target and write its manifest
patlas init <folder>                          drop a starter .patlas.json
patlas view <folder> [--port N] [--no-open]   analyze and open the map in a browser
```

`view` is the everyday command: it analyzes the folder, serves the viewer plus the generated manifest on a local server, and opens your browser.

## What you'll see in the map

The viewer opens on a chooser and lets you read the system at four altitudes, descending from the whole system to a single step:

- **Deployment** — the promotion spine: which pipelines deploy to which environments, with 🛑 markers on gated (approval-required) environments.
- **Policy** — the pipelines that govern the repo (branch policies, checks) rather than deploy code.
- **Subsystems** — the target's files grouped into clusters, with the edges between them.
- **All files** — a browsable grid of every parsed node.

From any node: **double-click** (or right-click → *Open steps*) to drill into a pipeline's stage → job → step chain; **click** for the side panel (Documentation, Details, Source). The side panel shows plain-language docs seeded from the file's own comments, work-item tags as clickable links, parameters, and the raw file with syntax highlighting. **Ctrl/Cmd-click** or **Shift-click** a node to open its source file. **Back** restores the previous view's zoom and position.

## Try it on the sample

The repo ships a tiny synthetic pipeline folder at `fixtures/sample` that exercises the core edges (an entry pipeline → an included build template → a deploy template with an environment, plus a deliberately-unresolved reference to prove flagging). It's the quickest way to see the map:

```bash
dotnet run --project src/PipelineAtlas.Cli -- view ./fixtures/sample
```

## Point it at your own pipelines

```bash
patlas init  C:\path\to\your\pipelines   # writes a starter .patlas.json you can edit
patlas view  C:\path\to\your\pipelines   # analyze and open the map
```

Edit the generated `.patlas.json` (see below) to set your scan globs, work-item base URL, and how files cluster into subsystems, then re-run `view`. The tool only ever **reads** the target folder — it never writes into it.

## Build & run (developers)

Prerequisites: the .NET 10 SDK, and Node.js (only to build the viewer).

```bash
# build the viewer once (the CLI build also does this automatically if dist is missing)
npm install --prefix src/PipelineAtlas.App

# build everything and run against the sample fixture
dotnet build
dotnet run --project src/PipelineAtlas.Cli -- view ./fixtures/sample
```

Run the tests with `dotnet test`. Regenerate the golden manifest with `UPDATE_GOLDEN=1 dotnet test`.

## Export a standalone executable

`patlas` publishes as a self-contained, single-file executable — the end user needs **neither .NET nor Node installed**. The viewer is embedded inside the exe.

```bash
dotnet publish src/PipelineAtlas.Cli -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish
```

Then, on any machine:

```
patlas view C:\path\to\a\pipeline\folder
```

(Use the matching runtime identifier for other targets, e.g. `linux-x64`, `osx-arm64`. Pass `-p:BuildViewer=false` to skip the automatic viewer build when a CI stage builds it separately.)

## The target contract — `.patlas.json`

Every folder Pipeline Atlas analyzes carries a `.patlas.json` at its root; it is how a target self-describes so the engine stays generic (scan globs, work-item link patterns, subsystem clusters, node status). Run `patlas init <folder>` to drop a starter you can edit. Work-item links are **best-effort**: until `workItems.baseUrl` is set to your real Azure DevOps org/project, tags still parse and display — they just aren't clickable.
