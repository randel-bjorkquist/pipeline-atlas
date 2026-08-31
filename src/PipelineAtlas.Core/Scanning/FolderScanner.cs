using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Extensions.FileSystemGlobbing.Abstractions;
using PipelineAtlas.Core.Configuration;
using PipelineAtlas.Core.Util;

namespace PipelineAtlas.Core.Scanning;

// File discovery. Globs come from .patlas.json (CLAUDE.md sec 4); the config file
// itself is never a node. Returns target-relative posix paths, sorted, so the scan
// order is deterministic.
public static class FolderScanner
{
    public static IReadOnlyList<string> Scan(string targetDir, ScanConfig scan)
    {
        var matcher = new Matcher(StringComparison.OrdinalIgnoreCase);
        matcher.AddIncludePatterns(scan.Include);
        matcher.AddExcludePatterns(scan.Exclude);
        matcher.AddExclude(ConfigLoader.ConfigFilename);

        PatternMatchingResult result =
            matcher.Execute(new DirectoryInfoWrapper(new DirectoryInfo(targetDir)));

        return result.Files
            .Select(f => Globs.ToPosix(f.Path))
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToArray();
    }
}
