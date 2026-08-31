using YamlDotNet.RepresentationModel;

namespace PipelineAtlas.Core.Parsing;

// Thin, null-tolerant accessors over YamlDotNet's representation model, so the
// parser can walk pipeline YAML the way the TypeScript port walked plain objects.
internal static class Yaml
{
    public static YamlMappingNode? AsMap(YamlNode? node) => node as YamlMappingNode;

    public static IReadOnlyList<YamlNode> Seq(YamlNode? node) =>
        node is YamlSequenceNode s ? [.. s.Children] : [];

    public static string? Scalar(YamlNode? node) => (node as YamlScalarNode)?.Value;

    public static YamlNode? Get(YamlMappingNode? map, string key)
    {
        if (map is null)
        {
            return null;
        }

        foreach (KeyValuePair<YamlNode, YamlNode> kv in map.Children)
        {
            if (kv.Key is YamlScalarNode k && k.Value == key)
            {
                return kv.Value;
            }
        }

        return null;
    }

    public static string? GetScalar(YamlMappingNode? map, string key) => Scalar(Get(map, key));

    public static bool Has(YamlMappingNode? map, string key) => Get(map, key) is not null;
}
