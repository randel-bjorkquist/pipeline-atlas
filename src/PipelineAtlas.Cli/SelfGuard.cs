namespace PipelineAtlas.Cli;

// Self-analysis guard (CLAUDE.md sec 8.2). The engine must scan the folder passed
// in, never the Pipeline Atlas repo itself. A target that contains the core project
// is almost certainly this tool; refuse unless the user explicitly overrides.
public static class SelfGuard
{
    public static bool LooksLikeSelf(string targetDir) =>
        File.Exists(Path.Combine(targetDir, "src", "PipelineAtlas.Core", "PipelineAtlas.Core.csproj"))
        || File.Exists(Path.Combine(targetDir, "PipelineAtlas.slnx"));

    public static void AssertNotSelf(string targetDir, bool allowSelf)
    {
        if (allowSelf || !LooksLikeSelf(targetDir))
        {
            return;
        }

        throw new InvalidOperationException(
            $"Target \"{targetDir}\" looks like the Pipeline Atlas repo itself. " +
            "Point patlas at a folder of pipeline files instead, or pass --allow-self to override.");
    }
}
