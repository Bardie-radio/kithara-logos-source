namespace Bardie.Module.Source;

/// <summary>
/// Opaque library blob keys under <c>tunes/&lt;source_slug&gt;/…</c> (module-side helper;
/// Kithara enforces ownership on Put/Get).
/// </summary>
public static class ModuleBlobKeys
{
    public const string TunesSegment = "tunes";

    public static string ModulePrefix(string sourceSlug)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceSlug);
        return $"{TunesSegment}/{sourceSlug.Trim().ToLowerInvariant()}/";
    }

    /// <summary>Builds <c>tunes/&lt;slug&gt;/&lt;objectId&gt;</c>.</summary>
    public static string ForObject(string sourceSlug, string objectId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceSlug);
        ArgumentException.ThrowIfNullOrWhiteSpace(objectId);

        var id = objectId.Trim().Trim('/');
        if (id.Contains("..", StringComparison.Ordinal)
            || id.Contains('\\')
            || id.Contains('\0'))
        {
            throw new ArgumentException("Object id must not contain path traversal.", nameof(objectId));
        }

        return $"{ModulePrefix(sourceSlug)}{id}";
    }
}
