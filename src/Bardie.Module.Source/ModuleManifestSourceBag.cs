using System.Text.Json;
using Bardie.Logos.Channel.Manifest;

namespace Bardie.Module.Source;

/// <summary>
/// Parses the opaque <c>source</c> bag from <see cref="ModuleManifest.Extensions"/>
/// (ModuleChannel does not type kind-specific bags).
/// </summary>
public static class ModuleManifestSourceBag
{
    public const string ExtensionKey = "source";

    /// <summary>
    /// Reads <c>source.searchFields</c> from the manifest. Empty when absent or malformed.
    /// </summary>
    public static IReadOnlyList<SourceSearchFieldOptions> ReadSearchFields(ModuleManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        if (manifest.Extensions is null
            || !manifest.Extensions.TryGetValue(ExtensionKey, out var sourceElement)
            || sourceElement.ValueKind != JsonValueKind.Object)
        {
            return [];
        }

        if (!sourceElement.TryGetProperty("searchFields", out var fieldsElement)
            && !sourceElement.TryGetProperty("search_fields", out fieldsElement))
        {
            return [];
        }

        if (fieldsElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var fields = new List<SourceSearchFieldOptions>();
        foreach (var item in fieldsElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var name = ReadString(item, "name") ?? ReadString(item, "Name");
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            fields.Add(new SourceSearchFieldOptions
            {
                Name = name.Trim(),
                Required = ReadBool(item, "required") ?? ReadBool(item, "Required") ?? false,
            });
        }

        return fields;
    }

    private static string? ReadString(JsonElement obj, string propertyName) =>
        obj.TryGetProperty(propertyName, out var el) && el.ValueKind == JsonValueKind.String
            ? el.GetString()
            : null;

    private static bool? ReadBool(JsonElement obj, string propertyName) =>
        obj.TryGetProperty(propertyName, out var el) && (el.ValueKind is JsonValueKind.True or JsonValueKind.False)
            ? el.GetBoolean()
            : null;
}
