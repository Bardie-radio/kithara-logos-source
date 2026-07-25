using Bardie.Logos.Channel.Manifest;
using Microsoft.Extensions.Logging;

namespace Bardie.Module.Source;

/// <summary>Local temp file obtained from blob cache or a fresh download.</summary>
public sealed class CachedMediaFile : IAsyncDisposable
{
    private readonly bool _ownsPath;

    public CachedMediaFile(string path, bool fromCache, bool ownsPath = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        Path = path;
        FromCache = fromCache;
        _ownsPath = ownsPath;
    }

    public string Path { get; }

    public bool FromCache { get; }

    public ValueTask DisposeAsync()
    {
        if (_ownsPath)
        {
            TryDelete(Path);
        }

        return ValueTask.CompletedTask;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // best-effort
        }
    }
}

/// <summary>
/// Cache-first blob + <c>Library.EnsureTune</c> handshake used by Magpie/Catbird-style modules.
/// </summary>
public sealed class ModuleTuneCache
{
    private readonly IModuleBlobStorageClient _blobs;
    private readonly IModuleLibraryClient _library;
    private readonly ModuleManifest _manifest;
    private readonly ILogger<ModuleTuneCache> _logger;

    public ModuleTuneCache(
        IModuleBlobStorageClient blobs,
        IModuleLibraryClient library,
        ModuleManifest manifest,
        ILogger<ModuleTuneCache> logger)
    {
        _blobs = blobs;
        _library = library;
        _manifest = manifest;
        _logger = logger;
    }

    public string ObjectKey(string externalId) =>
        ModuleBlobKeys.ForObject(_manifest.Slug, externalId);

    /// <summary>
    /// On hit: materializes blob bytes to a temp file.
    /// On miss: runs <paramref name="downloadAsync"/> into a temp file, Puts the blob, EnsureTune, returns that path.
    /// Caller must dispose the result to delete the temp file.
    /// </summary>
    public async Task<CachedMediaFile> OpenOrFetchAsync(
        EnsureTuneCommand metadata,
        Func<Stream, CancellationToken, Task> downloadAsync,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentException.ThrowIfNullOrWhiteSpace(metadata.ExternalId);
        ArgumentNullException.ThrowIfNull(downloadAsync);

        var key = string.IsNullOrWhiteSpace(metadata.StorageKey)
            ? ObjectKey(metadata.ExternalId)
            : metadata.StorageKey!;

        if (await _blobs.ExistsAsync(key, cancellationToken).ConfigureAwait(false))
        {
            _logger.LogInformation("Cache hit for {ExternalId} ({Key})", metadata.ExternalId, key);
            await using var cached = await _blobs.GetAsync(key, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException($"Blob '{key}' vanished after Exists.");
            var path = await CopyToTempAsync(cached.Stream, metadata.ExternalId, cancellationToken)
                .ConfigureAwait(false);
            return new CachedMediaFile(path, fromCache: true);
        }

        _logger.LogInformation("Cache miss for {ExternalId}; downloading", metadata.ExternalId);
        var downloadPath = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"bardie-dl-{_manifest.Slug}-{metadata.ExternalId}-{Guid.NewGuid():N}");

        try
        {
            await using (var download = new FileStream(
                             downloadPath,
                             FileMode.Create,
                             FileAccess.ReadWrite,
                             FileShare.Read,
                             64 * 1024,
                             FileOptions.Asynchronous))
            {
                await downloadAsync(download, cancellationToken).ConfigureAwait(false);
                download.Position = 0;
                var put = await _blobs.PutAsync(
                        download,
                        key: key,
                        contentType: string.IsNullOrWhiteSpace(metadata.ContentType)
                            ? "application/octet-stream"
                            : metadata.ContentType,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                await _library.EnsureTuneAsync(
                        metadata with
                        {
                            StorageKey = put.Key,
                            SizeBytes = put.SizeBytes,
                            ContentType = string.IsNullOrWhiteSpace(metadata.ContentType)
                                ? "application/octet-stream"
                                : metadata.ContentType,
                            ModuleSlug = string.IsNullOrWhiteSpace(metadata.ModuleSlug)
                                ? _manifest.Slug
                                : metadata.ModuleSlug,
                        },
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            return new CachedMediaFile(downloadPath, fromCache: false);
        }
        catch
        {
            TryDelete(downloadPath);
            throw;
        }
    }

    private static async Task<string> CopyToTempAsync(
        Stream source,
        string externalId,
        CancellationToken cancellationToken)
    {
        var path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"bardie-cache-{externalId}-{Guid.NewGuid():N}");
        await using var file = new FileStream(
            path,
            FileMode.Create,
            FileAccess.Write,
            FileShare.Read,
            64 * 1024,
            FileOptions.Asynchronous);
        await source.CopyToAsync(file, cancellationToken).ConfigureAwait(false);
        return path;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // best-effort
        }
    }
}
