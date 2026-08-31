using System.Reflection;
using System.Text.Json;
using Json.Schema;
using PipelineAtlas.Core.Model;
using PipelineAtlas.Core.Serialization;

namespace PipelineAtlas.Core.Building;

// Schema validation. manifest.json is validated against the embedded
// manifest.schema.json on every build (CLAUDE.md sec 5) so the contract the app
// renders from can't drift.
public static class ManifestValidator
{
    private static readonly JsonSchema Schema = LoadSchema();

    public static IReadOnlyList<string> Validate(Manifest manifest)
    {
        JsonElement element = JsonSerializer.SerializeToElement(manifest, ManifestJson.Options);
        EvaluationResults results = Schema.Evaluate(
            element,
            new EvaluationOptions { OutputFormat = OutputFormat.List });

        if (results.IsValid)
        {
            return [];
        }

        var errors = new List<string>();
        Collect(results, errors);
        return errors.Count > 0 ? errors : ["manifest failed schema validation"];
    }

    /// <summary>Validate and throw with a readable message if the manifest is malformed.</summary>
    public static void AssertValid(Manifest manifest)
    {
        IReadOnlyList<string> errors = Validate(manifest);
        if (errors.Count > 0)
        {
            throw new InvalidOperationException(
                "Manifest failed schema validation:" + Environment.NewLine + "  " +
                string.Join(Environment.NewLine + "  ", errors));
        }
    }

    private static void Collect(EvaluationResults results, List<string> errors)
    {
        if (results.Errors is { Count: > 0 } dict)
        {
            string location = results.InstanceLocation.ToString();
            foreach (KeyValuePair<string, string> error in dict)
            {
                errors.Add($"{(location.Length == 0 ? "/" : location)} {error.Value}");
            }
        }

        foreach (EvaluationResults child in results.Details ?? [])
        {
            Collect(child, errors);
        }
    }

    private static JsonSchema LoadSchema()
    {
        Assembly assembly = typeof(ManifestValidator).Assembly;
        string resourceName = Array.Find(
            assembly.GetManifestResourceNames(),
            n => n.EndsWith("manifest.schema.json", StringComparison.Ordinal))
            ?? throw new InvalidOperationException("Embedded manifest.schema.json not found.");

        using Stream stream = assembly.GetManifestResourceStream(resourceName)!;
        using var reader = new StreamReader(stream);
        return JsonSchema.FromText(reader.ReadToEnd());
    }
}
