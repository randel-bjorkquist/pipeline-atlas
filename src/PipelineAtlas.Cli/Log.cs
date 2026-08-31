namespace PipelineAtlas.Cli;

// Tiny leveled logger. Everything goes to stderr so stdout stays clean for any
// piped/machine output. Best-effort notices (e.g. work-item linking) are Info and
// must never escalate to warn/error.
public static class Log
{
    private static bool _quiet;

    public static void SetQuiet(bool value) => _quiet = value;

    public static void Info(string message)
    {
        if (!_quiet) Console.Error.WriteLine($"info  {message}");
    }

    public static void Warn(string message) => Console.Error.WriteLine($"warn  {message}");

    public static void Error(string message) => Console.Error.WriteLine($"error {message}");

    public static void Success(string message)
    {
        if (!_quiet) Console.Error.WriteLine($"ok    {message}");
    }
}
