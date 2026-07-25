using Bardie.Logos.Channel.Participant;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Bardie.Module.Source;

public static class SourceModuleServiceCollectionExtensions
{
    /// <summary>
    /// Registers search-field options + Register customizer, track-job registry, FIFO sink,
    /// and Kithara BlobStorage / Library dial clients.
    /// Requires <see cref="AddModuleParticipant"/> (or Hosting bootstrap) already wired.
    /// </summary>
    public static IServiceCollection AddSourceModuleDefaults(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<SourceModuleOptions>(configuration.GetSection(SourceModuleOptions.SectionName));
        services.AddSingleton<IModuleRegisterRequestCustomizer, SourceSearchFieldsRegisterRequestCustomizer>();
        services.AddSingleton<ITrackJobRegistry, TrackJobRegistry>();
        services.AddSingleton<IFifoAudioSink, FifoAudioSink>();
        services.AddSingleton<IModuleBlobStorageClient, ModuleBlobStorageClient>();
        services.AddSingleton<IModuleLibraryClient, ModuleLibraryClient>();
        services.AddSingleton<ModuleTuneCache>();
        return services;
    }
}
