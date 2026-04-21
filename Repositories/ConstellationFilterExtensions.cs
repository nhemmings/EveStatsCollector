using EveStatsCollector.Repositories.InMemory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EveStatsCollector.Repositories;

public static class ConstellationFilterExtensions
{
    public static IServiceCollection AddConstellationFilter(
        this IServiceCollection services,
        IConfiguration config)
    {
        var names = (config.GetSection("Filter:Constellations").Get<string[]>() ?? [])
            .Where(n => !string.Equals(n, "all", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (names.Count == 0)
            return services;

        services.AddSingleton<ConstellationFilter>(sp =>
            new ConstellationFilter(sp.GetRequiredService<IConstellationRepository>(), names));

        services.Replace(ServiceDescriptor.Singleton<IKillsReportRepository>(sp =>
            new FilteredKillsReportRepository(
                new InMemoryKillsReportRepository(),
                sp.GetRequiredService<ConstellationFilter>())));

        services.Replace(ServiceDescriptor.Singleton<IJumpsReportRepository>(sp =>
            new FilteredJumpsReportRepository(
                new InMemoryJumpsReportRepository(),
                sp.GetRequiredService<ConstellationFilter>())));

        return services;
    }
}
