using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

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

        services.AddSingleton<ConstellationFilter>(sp =>
            new ConstellationFilter(sp.GetRequiredService<IConstellationRepository>(), names));

        return services;
    }
}
