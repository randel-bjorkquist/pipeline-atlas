using System.IO.Compression;
using System.Reflection;

namespace PipelineAtlas.Cli;

// The built viewer travels inside the assembly as a single embedded zip
// (viewer.zip). This unpacks it once into memory and serves entries by path, so
// `patlas view` can serve the whole UI with no files on disk.
public static class ViewerAssets
{
    private static readonly Dictionary<string, byte[]> Files = Load();

    public static bool Any => Files.Count > 0;

    public static (byte[] Bytes, string ContentType)? Get(string? path)
    {
        string key = string.IsNullOrEmpty(path) ? "index.html" : path.TrimStart('/');
        if (!Files.TryGetValue(key, out byte[]? bytes) && !Path.HasExtension(key))
        {
            // Unknown path with no file extension: fall back to the SPA entry.
            Files.TryGetValue("index.html", out bytes);
        }

        return bytes is null ? null : (bytes, ContentType(key));
    }

    private static Dictionary<string, byte[]> Load()
    {
        var map = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        Assembly asm = typeof(ViewerAssets).Assembly;
        string? resource = Array.Find(
            asm.GetManifestResourceNames(),
            n => n.EndsWith("viewer.zip", StringComparison.Ordinal));
        if (resource is null)
        {
            return map;
        }

        using Stream stream = asm.GetManifestResourceStream(resource)!;
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        foreach (ZipArchiveEntry entry in zip.Entries)
        {
            if (entry.FullName.EndsWith('/'))
            {
                continue; // directory entry
            }

            using Stream es = entry.Open();
            using var ms = new MemoryStream();
            es.CopyTo(ms);
            map[entry.FullName.Replace('\\', '/')] = ms.ToArray();
        }

        return map;
    }

    private static string ContentType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".html" => "text/html; charset=utf-8",
        ".js" => "text/javascript; charset=utf-8",
        ".css" => "text/css; charset=utf-8",
        ".json" => "application/json; charset=utf-8",
        ".svg" => "image/svg+xml",
        ".ico" => "image/x-icon",
        ".png" => "image/png",
        ".woff2" => "font/woff2",
        ".woff" => "font/woff",
        ".map" => "application/json",
        _ => "application/octet-stream",
    };
}
