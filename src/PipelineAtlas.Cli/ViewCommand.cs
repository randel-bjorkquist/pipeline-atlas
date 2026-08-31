using System.Diagnostics;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PipelineAtlas.Core;
using PipelineAtlas.Core.Serialization;

namespace PipelineAtlas.Cli;

// `patlas view <folder>` — analyze a target, then serve the embedded viewer plus
// the freshly generated manifest on a local Kestrel server and open the browser.
// The app reads only manifest.json (CLAUDE.md sec 8.4); nothing touches the repo
// or Azure DevOps at runtime.
public static class ViewCommand
{
    public static int Run(IReadOnlyList<string> argv)
    {
        ParsedArgs args = ArgParser.Parse(argv);
        if (args.Positionals.Count == 0)
        {
            throw new InvalidOperationException("view needs a <folder>. Usage: patlas view <folder> [--port N] [--no-open]");
        }

        string targetDir = Path.GetFullPath(args.Positionals[0]);
        if (!Directory.Exists(targetDir))
        {
            throw new InvalidOperationException($"Not a folder: {targetDir}");
        }

        SelfGuard.AssertNotSelf(targetDir, args.Has("allow-self"));

        if (!ViewerAssets.Any)
        {
            throw new InvalidOperationException(
                "The viewer is not embedded in this build. Build the app first " +
                "(npm run build in src/PipelineAtlas.App), then rebuild the CLI.");
        }

        var manifest = Analyzer.Analyze(targetDir, new AnalyzeOptions { OnInfo = Log.Info });
        string manifestJson = JsonSerializer.Serialize(manifest, ManifestJson.Options);

        int port = int.TryParse(args.Value("port"), out int p) ? p : 0;

        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls($"http://127.0.0.1:{port}");
        WebApplication app = builder.Build();

        string root = targetDir.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        app.MapGet("/manifest.json", () => Results.Text(manifestJson, "application/json; charset=utf-8"));
        app.MapGet("/source", (string? path) =>
        {
            // Serve a raw target file for "View file" (read-only, inside the target).
            if (string.IsNullOrEmpty(path))
            {
                return Results.BadRequest();
            }

            string full = Path.GetFullPath(Path.Combine(root, path));
            if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !File.Exists(full))
            {
                return Results.NotFound();
            }

            return Results.Text(File.ReadAllText(full), "text/plain; charset=utf-8");
        });
        app.MapGet("/", () => Serve(""));
        app.MapGet("/{**path}", (string path) => Serve(path));

        app.Start();
        string url = app.Urls.First();

        Log.Success($"Serving {manifest.Target.DisplayName} at {url}");
        Log.Info($"{manifest.Nodes.Count} nodes, {manifest.Edges.Count} edges, {manifest.Flows.Count} flows. Press Ctrl+C to stop.");

        if (!args.Has("no-open"))
        {
            OpenBrowser(url);
        }

        app.WaitForShutdown();
        return 0;
    }

    private static IResult Serve(string? path)
    {
        (byte[] Bytes, string ContentType)? asset = ViewerAssets.Get(path);
        return asset is null
            ? Results.NotFound()
            : Results.Bytes(asset.Value.Bytes, asset.Value.ContentType);
    }

    private static void OpenBrowser(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
        }
        catch
        {
            Log.Info($"Open your browser to {url}");
        }
    }
}
