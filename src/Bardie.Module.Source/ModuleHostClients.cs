using Bardie.Library.V1;
using Bardie.Logos.Channel.Manifest;
using Bardie.Logos.Channel.Participant;
using Bardie.Storage.V1;
using Google.Protobuf;
using Grpc.Core;
using Microsoft.Extensions.Options;

namespace Bardie.Module.Source;

/// <summary>Thin BlobStorage client — modules dial Kithara over participant mTLS.</summary>
public interface IModuleBlobStorageClient
{
    /// <summary>
    /// Puts <paramref name="content"/>; empty <paramref name="key"/> lets Kithara assign under
    /// <c>tunes/&lt;slug&gt;/…</c>.
    /// </summary>
    Task<PutBlobResult> PutAsync(
        Stream content,
        string? key = null,
        string? contentType = null,
        CancellationToken cancellationToken = default);

    Task<BlobGetResult?> GetAsync(string key, CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(string key, CancellationToken cancellationToken = default);
}

public sealed record PutBlobResult(string Key, long SizeBytes);

public sealed record BlobGetResult(Stream Stream, string? ContentType) : IAsyncDisposable, IDisposable
{
    public void Dispose() => Stream.Dispose();

    public ValueTask DisposeAsync() => Stream.DisposeAsync();
}

/// <summary>Thin Library.EnsureTune client — modules dial Kithara over participant mTLS.</summary>
public interface IModuleLibraryClient
{
    Task<EnsureTuneResult> EnsureTuneAsync(
        EnsureTuneCommand command,
        CancellationToken cancellationToken = default);
}

public sealed record EnsureTuneCommand(
    string ExternalId,
    string? Title = null,
    string? Artist = null,
    double? DurationSeconds = null,
    string? ArtworkUrl = null,
    string? StorageKey = null,
    string? ContentType = null,
    long? SizeBytes = null,
    string? ModuleSlug = null);

public sealed record EnsureTuneResult(Guid TuneId, bool Created);

public sealed class ModuleBlobStorageClient : IModuleBlobStorageClient
{
    private const int ChunkSize = 64 * 1024;

    private readonly IModuleParticipantChannelFactory _channels;
    private readonly ModuleParticipantOptions _participant;

    public ModuleBlobStorageClient(
        IModuleParticipantChannelFactory channels,
        IOptions<ModuleParticipantOptions> participant)
    {
        _channels = channels;
        _participant = participant.Value;
    }

    public async Task<PutBlobResult> PutAsync(
        Stream content,
        string? key = null,
        string? contentType = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);

        using var channel = _channels.CreateMtlsChannel(_participant.HostGrpcAddress);
        var client = new BlobStorage.BlobStorageClient(channel);
        using var call = client.Put(cancellationToken: cancellationToken);

        await call.RequestStream.WriteAsync(
                new PutBlobRequest
                {
                    Header = new PutBlobHeader
                    {
                        Key = key ?? string.Empty,
                        ContentType = contentType ?? string.Empty,
                    },
                },
                cancellationToken)
            .ConfigureAwait(false);

        var buffer = new byte[ChunkSize];
        int read;
        while ((read = await content.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)
                   .ConfigureAwait(false)) > 0)
        {
            await call.RequestStream.WriteAsync(
                    new PutBlobRequest { Chunk = ByteString.CopyFrom(buffer, 0, read) },
                    cancellationToken)
                .ConfigureAwait(false);
        }

        await call.RequestStream.CompleteAsync().ConfigureAwait(false);
        var response = await call.ResponseAsync.ConfigureAwait(false);
        return new PutBlobResult(response.Key, response.SizeBytes);
    }

    public async Task<BlobGetResult?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        using var channel = _channels.CreateMtlsChannel(_participant.HostGrpcAddress);
        var client = new BlobStorage.BlobStorageClient(channel);

        try
        {
            using var call = client.Get(new GetBlobRequest { Key = key }, cancellationToken: cancellationToken);
            var memory = new MemoryStream();
            string? contentType = null;

            await foreach (var chunk in call.ResponseStream.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                if (!string.IsNullOrEmpty(chunk.ContentType))
                {
                    contentType = chunk.ContentType;
                }

                if (chunk.Chunk.Length > 0)
                {
                    chunk.Chunk.WriteTo(memory);
                }
            }

            memory.Position = 0;
            return new BlobGetResult(memory, contentType);
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        using var channel = _channels.CreateMtlsChannel(_participant.HostGrpcAddress);
        var client = new BlobStorage.BlobStorageClient(channel);
        var response = await client.ExistsAsync(
                new ExistsBlobRequest { Key = key },
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return response.Exists;
    }

    public async Task<bool> DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        using var channel = _channels.CreateMtlsChannel(_participant.HostGrpcAddress);
        var client = new BlobStorage.BlobStorageClient(channel);
        var response = await client.DeleteAsync(
                new DeleteBlobRequest { Key = key },
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return response.Deleted;
    }
}

public sealed class ModuleLibraryClient : IModuleLibraryClient
{
    private readonly IModuleParticipantChannelFactory _channels;
    private readonly ModuleParticipantOptions _participant;
    private readonly ModuleManifest _manifest;

    public ModuleLibraryClient(
        IModuleParticipantChannelFactory channels,
        IOptions<ModuleParticipantOptions> participant,
        ModuleManifest manifest)
    {
        _channels = channels;
        _participant = participant.Value;
        _manifest = manifest;
    }

    public async Task<EnsureTuneResult> EnsureTuneAsync(
        EnsureTuneCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.ExternalId);

        using var channel = _channels.CreateMtlsChannel(_participant.HostGrpcAddress);
        var client = new Bardie.Library.V1.Library.LibraryClient(channel);
        var request = new EnsureTuneRequest
        {
            ModuleSlug = string.IsNullOrWhiteSpace(command.ModuleSlug)
                ? _manifest.Slug
                : command.ModuleSlug.Trim().ToLowerInvariant(),
            ExternalId = command.ExternalId,
            Title = command.Title ?? string.Empty,
            Artist = command.Artist ?? string.Empty,
            DurationSeconds = command.DurationSeconds ?? 0,
            ArtworkUrl = command.ArtworkUrl ?? string.Empty,
            StorageKey = command.StorageKey ?? string.Empty,
            ContentType = command.ContentType ?? string.Empty,
            SizeBytes = command.SizeBytes ?? 0,
        };

        var response = await client.EnsureTuneAsync(request, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (!Guid.TryParse(response.TuneId, out var tuneId))
        {
            throw new InvalidOperationException($"Library returned invalid tune_id '{response.TuneId}'.");
        }

        return new EnsureTuneResult(tuneId, response.Created);
    }
}
