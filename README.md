# pipeline-atlas

Pipeline Atlas turns a folder of Azure DevOps pipeline YAML and PowerShell into an interactive, zoomable map. Point it at any pipeline folder and navigate the system from a 30,000-ft overview down to individual steps, with plain-language docs and work-item tags at every level. Folder in, map out.

## What it is

- **Folder in, map out.** The input is a path you pass in, never a fixed location.
- **Project-agnostic.** Nothing about a specific repo is baked into the tool; a target folder describes itself through a small `.patlas.json`.
- **The map is a projection.** The engine parses the folder into one `manifest.json`; the viewer renders only that manifest. Regenerate, and the picture updates.

## Getting started

**Prerequisites:** the [.NET 10 SDK](https://dotnet.microsoft.com/download) and [Node.js 18+](https://nodejs.org) (npm compiles the web viewer). That is the entire setup — there are no manual install or configuration steps.

```bash
git clone https://github.com/randel-bjorkquist/pipeline-atlas.git
cd pipeline-atlas
dotnet build                                                    # also builds & embeds the viewer
dotnet run --project src/PipelineAtlas.Cli -- view ./fixtures/sample
```

`dotnet build` compiles the viewer for you: the first build runs `npm ci` + `npm run build` under `src/PipelineAtlas.App` automatically (so it takes a little longer), then embeds the result in the CLI; later builds reuse it. If Node.js isn't installed, the build stops with a message telling you to install it. That's the only thing you have to provide yourself.

### How you run it (important)

There is **no `patlas` command until you build the standalone executable** — so typing `patlas ...` in a fresh checkout gives *"the term 'patlas' is not recognized"*. From a source checkout you run the tool through the .NET SDK:

```bash
dotnet run --project src/PipelineAtlas.Cli -- <args>
# e.g.
dotnet run --project src/PipelineAtlas.Cli -- view ./fixtures/sample
```

Throughout this README, **`patlas <args>` is shorthand** for that longer form. You get an actual `patlas` command in one of two ways:

- **Publish the executable** (see [Export a standalone executable](#export-a-standalone-executable)) and add the `publish` folder to your `PATH`. Then `patlas <args>` works anywhere.
- **Or, for a source checkout, add a shortcut for your shell session.** In PowerShell:

  ```powershell
  function patlas { dotnet run --project src/PipelineAtlas.Cli -- @args }
  ```

  (Add that line to your PowerShell profile to make it stick.) Now `patlas view ./fixtures/sample` runs from source. Everywhere below that shows `patlas ...`, either do this, publish the exe, or substitute `dotnet run --project src/PipelineAtlas.Cli -- ...`.

## Layout

- `src/PipelineAtlas.Core` — the engine (C#/.NET 10 class library): a folder path (+ `.patlas.json`) → a validated `manifest.json`.
- `src/PipelineAtlas.Cli` — the `patlas` console app: `analyze` / `init` / `view`.
- `src/PipelineAtlas.App` — the viewer (React + Vite + React Flow); builds to static assets embedded in the exe.
- `tests/PipelineAtlas.Core.Tests` — xUnit golden-file tests over `fixtures/sample`.

## Commands

(`patlas` is shorthand — see [How you run it](#how-you-run-it-important). From a source checkout, replace `patlas` with `dotnet run --project src/PipelineAtlas.Cli --`.)

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

### Keeping the config out of the target folder

By default the `.patlas.json` lives at the root of the folder you analyze. If that folder is itself source-controlled (or otherwise read-only) and you don't want to commit a `.patlas.json` into it, keep the config **anywhere else** and point at it with `--config`:

```bash
# create the starter under inputs/ in your Pipeline Atlas checkout, not in the target
patlas init C:\Source\PHA-Web\pipelines --config .\inputs\pha-web.patlas.json

# then analyze / view the untouched target, supplying the external config
patlas view C:\Source\PHA-Web\pipelines --config .\inputs\pha-web.patlas.json
```

The glob and path values inside the config are always interpreted **relative to the target folder**, wherever the file itself lives. Keeping these configs under `inputs/` is the recommended convention: that folder is git-ignored, so your target descriptions stay local and never land in this repo. (The tool only ever reads the target — it never copies it or writes into it.)

## Build & test (developers)

Setup is covered in [Getting started](#getting-started) above — `dotnet build` handles the viewer automatically. A few extra notes:

- To build just the CLI without the viewer (e.g. a CI stage that builds the viewer separately), pass `-p:BuildViewer=false`.
- Run the tests with `dotnet test`. Regenerate the golden manifest with `UPDATE_GOLDEN=1 dotnet test`.

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

### Install `patlas` on your PATH

Publishing drops the executable at `publish/patlas.exe` (or `publish/patlas` on macOS/Linux). Put its folder on your `PATH` and you can type `patlas` from any terminal — no `dotnet run`, no per-session shortcut.

**Windows (PowerShell):**

```powershell
# 1. publish (from the repo root)
dotnet publish src/PipelineAtlas.Cli -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish

# 2. copy the exe to a stable location
$dest = "$HOME\bin"
New-Item -ItemType Directory -Force $dest | Out-Null
Copy-Item .\publish\patlas.exe $dest -Force

# 3. add that folder to your user PATH (persists across sessions), once
$userPath = [Environment]::GetEnvironmentVariable('Path', 'User')
if ($userPath -notlike "*$dest*") {
  [Environment]::SetEnvironmentVariable('Path', "$userPath;$dest", 'User')
}
```

Open a **new** terminal (so it picks up the updated PATH), then verify:

```powershell
patlas --help
```

To use it in the *current* session without reopening, also run `$env:Path += ";$dest"`.

**macOS / Linux:** copy `publish/patlas` into a directory already on your `PATH` (e.g. `~/.local/bin` or `/usr/local/bin`) and make it executable:

```bash
install -m 0755 publish/patlas ~/.local/bin/patlas   # ensure ~/.local/bin is on your PATH
patlas --help
```

Once `patlas` is on your PATH, every `patlas ...` example in this README works verbatim — including against your own target, e.g. `patlas view C:\Source\PHA-Web\pipelines --config .\inputs\pha-web.patlas.json`.

## The target contract — `.patlas.json`

Every folder Pipeline Atlas analyzes carries a `.patlas.json` at its root; it is how a target self-describes so the engine stays generic (scan globs, work-item link patterns, subsystem clusters, node status). Run `patlas init <folder>` to drop a starter you can edit. Work-item links are **best-effort**: until `workItems.baseUrl` is set to your real Azure DevOps org/project, tags still parse and display — they just aren't clickable.
