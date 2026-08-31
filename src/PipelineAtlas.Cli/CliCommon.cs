namespace PipelineAtlas.Cli;

// Small helpers shared by the analyze/view commands.
public static class CliCommon
{
    // Resolve the optional --config value to a full path, verifying it exists.
    // Returns null when no --config was given (core then reads the target's own
    // .patlas.json). Lets the config live outside the read-only target folder.
    public static string? ResolveConfig(string? configValue)
    {
        if (string.IsNullOrEmpty(configValue))
        {
            return null;
        }

        string full = Path.GetFullPath(configValue);
        if (!File.Exists(full))
        {
            throw new InvalidOperationException(
                $"Config file not found: {full}. Pass --config <path-to-.patlas.json>.");
        }

        return full;
    }
}
