namespace PipelineAtlas.Cli;

// Minimal argument parser. Splits positionals from flags; supports "--flag",
// "--key value", "--key=value", and short "-o value". No external dependency.
public sealed class ParsedArgs
{
    public List<string> Positionals { get; } = [];
    public Dictionary<string, string> Flags { get; } = new(StringComparer.Ordinal);

    public bool Has(string flag) => Flags.ContainsKey(flag);
    public string? Value(string flag) => Flags.TryGetValue(flag, out string? v) ? v : null;
}

public static class ArgParser
{
    private static readonly HashSet<string> ValueFlags =
        new(StringComparer.Ordinal) { "-o", "--output", "--port", "--config" };

    public static ParsedArgs Parse(IReadOnlyList<string> argv)
    {
        var parsed = new ParsedArgs();
        for (int i = 0; i < argv.Count; i++)
        {
            string arg = argv[i];
            if (!arg.StartsWith('-'))
            {
                parsed.Positionals.Add(arg);
                continue;
            }

            int eq = arg.IndexOf('=');
            if (eq != -1)
            {
                parsed.Flags[Normalize(arg[..eq])] = arg[(eq + 1)..];
                continue;
            }

            if (ValueFlags.Contains(arg))
            {
                parsed.Flags[Normalize(arg)] = i + 1 < argv.Count ? argv[++i] : "";
                continue;
            }

            parsed.Flags[Normalize(arg)] = "true";
        }

        return parsed;
    }

    private static string Normalize(string flag)
    {
        string key = flag.TrimStart('-');
        return key == "o" ? "output" : key;
    }
}
