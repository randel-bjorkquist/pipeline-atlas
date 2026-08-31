using System.Text.Json;
using System.Text.Json.Serialization;

namespace PipelineAtlas.Core.Serialization;

// Canonical JSON settings for reading .patlas.json and writing manifest.json.
// camelCase property names, string enums (camelCase), null omitted, 2-space
// indent — the same options feed both the file output and schema validation so
// they can never disagree.
public static class ManifestJson
{
    public static readonly JsonSerializerOptions Options = Create();

    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = true,
            IndentSize = 2,
            NewLine = "\n", // LF so manifests diff byte-for-byte across OSes
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }
}
