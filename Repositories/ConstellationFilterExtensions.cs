using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EveStatsCollector.Repositories;

public static class ConstellationFilterExtensions
{
    public static IServiceCollection AddConstellationFilters(
        this IServiceCollection services,
        IConfiguration config)
    {
        var reportNames = ParseNames(config, "Filter:Report");
        var universeNames = ParseNames(config, "Filter:Universe")
            .Union(reportNames, StringComparer.OrdinalIgnoreCase)
            .ToList();

        services.AddSingleton(new UniverseConstellationFilter(universeNames));
        services.AddSingleton<ReportConstellationFilter>(sp =>
            new ReportConstellationFilter(sp.GetRequiredService<IConstellationRepository>(), reportNames));

        return services;
    }

    private static IReadOnlyList<string> ParseNames(IConfiguration config, string key) =>
        (config.GetSection(key).Get<string[]>() ?? [])
            .Where(n => !string.Equals(n, "all", StringComparison.OrdinalIgnoreCase))
            .ToList();
}
