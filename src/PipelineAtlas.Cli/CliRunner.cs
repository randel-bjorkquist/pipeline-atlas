namespace PipelineAtlas.Cli;

// patlas CLI router: analyze | init | view. `view` (serve the viewer on a
// generated manifest) lands with the viewer milestone.
public static class CliRunner
{
    private const string Help =
        """
        patlas - turn a folder of Azure DevOps pipeline files into a map.

        Usage:
          patlas analyze <folder> [-o manifest.json]   scan a target and write its manifest
          patlas init <folder> [--config <path>]        drop a starter .patlas.json
          patlas view <folder> [--port N]               analyze and open the viewer in a browser

        Flags:
          -o, --output <file>   manifest output path (analyze; default manifest.json)
          --config <file>       use a .patlas.json outside the target folder, so a
                                read-only/source-controlled target stays untouched
                                (analyze/view; init writes the starter there)
          --port <N>            port for the viewer server (view; default: auto)
          --no-open             don't open the browser automatically (view)
          --allow-self          allow analyzing the Pipeline Atlas repo itself (analyze/view)
          --force               overwrite an existing .patlas.json (init)
          --quiet               suppress info/ok output (errors still print)
          -h, --help            show this help

        """;

    public static int Run(string[] args)
    {
        if (args.Contains("--quiet"))
        {
            Log.SetQuiet(true);
        }

        string? command = args.Length > 0 ? args[0] : null;
        string[] rest = args.Skip(1).ToArray();

        try
        {
            switch (command)
            {
                case null:
                case "-h":
                case "--help":
                case "help":
                    Console.Out.Write(Help);
                    return 0;
                case "analyze":
                    return AnalyzeCommand.Run(rest);
                case "init":
                    return InitCommand.Run(rest);
                case "view":
                    return ViewCommand.Run(rest);
                default:
                    Log.Error($"unknown command \"{command}\". Try: patlas --help");
                    return 1;
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex.Message);
            return 1;
        }
    }
}
