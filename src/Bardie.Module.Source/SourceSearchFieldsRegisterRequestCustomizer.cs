using Bardie.Logos.Channel.Manifest;
using Bardie.Logos.Channel.Participant;
using Bardie.Modules.V1;
using Microsoft.Extensions.Options;

namespace Bardie.Module.Source;

/// <summary>Search field advertised on Module Registry Register (<c>Source.search_fields</c>).</summary>
public sealed class SourceSearchFieldOptions
{
    public string Name { get; set; } = string.Empty;
    public bool Required { get; set; }
}

/// <summary>
/// Shared source-module options. Prefer <c>source.searchFields</c> in <c>module.manifest.json</c>
/// for search schema; <see cref="SearchFields"/> is a fallback.
/// </summary>
public sealed class SourceModuleOptions
{
    public const string SectionName = "SourceModule";

    /// <summary>
    /// Fields advertised on Register when the manifest has none. When empty, defaults to mandatory <c>title</c>.
    /// </summary>
    public List<SourceSearchFieldOptions> SearchFields { get; set; } = [];

    /// <summary>
    /// Max concurrent track jobs in Running or Paused state.
    /// Values ≤ 0 mean unlimited.
    /// </summary>
    public int MaxParallelJobs { get; set; } = 4;
}

/// <summary>Attaches <c>Source.search_fields</c> on Module Registry Register.</summary>
public sealed class SourceSearchFieldsRegisterRequestCustomizer : IModuleRegisterRequestCustomizer
{
    private readonly SourceModuleOptions _options;

    public SourceSearchFieldsRegisterRequestCustomizer(IOptions<SourceModuleOptions> options)
    {
        _options = options.Value;
    }

    public void Customize(RegisterRequest request, ModuleManifest manifest)
    {
        var details = new SourceRegisterDetails();
        var fields = ModuleManifestSourceBag.ReadSearchFields(manifest);
        if (fields.Count == 0)
        {
            fields = _options.SearchFields;
        }

        if (fields.Count == 0)
        {
            details.SearchFields.Add(new SearchFieldDescriptor { Name = "title", Required = true });
        }
        else
        {
            foreach (var field in fields)
            {
                if (string.IsNullOrWhiteSpace(field.Name))
                {
                    continue;
                }

                details.SearchFields.Add(new SearchFieldDescriptor
                {
                    Name = field.Name.Trim(),
                    Required = field.Required,
                });
            }

            if (details.SearchFields.Count == 0)
            {
                details.SearchFields.Add(new SearchFieldDescriptor { Name = "title", Required = true });
            }
        }

        request.Source = details;
    }
}
