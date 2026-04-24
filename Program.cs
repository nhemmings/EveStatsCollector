using EveStatsCollector.Esi;
using EveStatsCollector.Repositories;
using EveStatsCollector.Repositories.InMemory;
using EveStatsCollector.Repositories.PostgreSql;
using EveStatsCollector.Services;
using Npgsql;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Events;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Debug()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var host = Host.CreateDefaultBuilder(args)
        .UseSerilog((ctx, _, cfg) => cfg
            .MinimumLevel.Debug()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("System.Net.Http", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
            .WriteTo.Seq(ctx.Configuration["Seq:ServerUrl"] ?? "http://localhost:5341"))
        .ConfigureServices((ctx, services) =>
        {
            var esiConfig = ctx.Configuration.GetSection("Esi");

            services.AddSingleton<EsiRateLimiter>();
            services.AddSingleton<EsiErrorTracker>();
            services.AddTransient<EsiRateLimitHandler>();

            services.AddHttpClient("ESI", client =>
            {
                client.BaseAddress = new Uri(esiConfig["BaseUrl"] ?? "https://esi.evetech.net/latest/");
                client.DefaultRequestHeaders.Add("Accept", "application/json");
                client.DefaultRequestHeaders.Add("User-Agent",
                    esiConfig["UserAgent"] ?? "EveStatsCollector/1.0");
            }).AddHttpMessageHandler<EsiRateLimitHandler>();

            services.AddSingleton<EsiClient>();

            var pgConnStr = ctx.Configuration.GetConnectionString("Postgres");
            if (!string.IsNullOrEmpty(pgConnStr))
            {
                services.AddSingleton(NpgsqlDataSource.Create(pgConnStr));

                services.AddSingleton<PostgreSqlRegionRepository>();
                services.AddSingleton<IRegionRepository>(sp => sp.GetRequiredService<PostgreSqlRegionRepository>());
                services.AddSingleton<IAsyncPreloadable>(sp => sp.GetRequiredService<PostgreSqlRegionRepository>());

                services.AddSingleton<PostgreSqlConstellationRepository>();
                services.AddSingleton<IConstellationRepository>(sp => sp.GetRequiredService<PostgreSqlConstellationRepository>());
                services.AddSingleton<IAsyncPreloadable>(sp => sp.GetRequiredService<PostgreSqlConstellationRepository>());

                services.AddSingleton<PostgreSqlSolarSystemRepository>();
                services.AddSingleton<ISolarSystemRepository>(sp => sp.GetRequiredService<PostgreSqlSolarSystemRepository>());
                services.AddSingleton<IAsyncPreloadable>(sp => sp.GetRequiredService<PostgreSqlSolarSystemRepository>());

                services.AddSingleton<IKillsReportRepository, PostgreSqlKillsReportRepository>();
                services.AddSingleton<IJumpsReportRepository, PostgreSqlJumpsReportRepository>();
            }
            else
            {
                services.AddSingleton<IRegionRepository, InMemoryRegionRepository>();
                services.AddSingleton<IConstellationRepository, InMemoryConstellationRepository>();
                services.AddSingleton<ISolarSystemRepository, InMemorySolarSystemRepository>();
                services.AddSingleton<IKillsReportRepository, InMemoryKillsReportRepository>();
                services.AddSingleton<IJumpsReportRepository, InMemoryJumpsReportRepository>();
            }

            services.AddConstellationFilters(ctx.Configuration);

            services.AddSingleton<UniverseService>();
            services.AddSingleton<StatsService>();
        })
        .Build();

    using var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) =>
    {
        e.Cancel = true;
        Log.Information("Shutdown requested");
        cts.Cancel();
    };

    foreach (var preloadable in host.Services.GetServices<IAsyncPreloadable>())
        await preloadable.LoadAsync(cts.Token);

    var universe = host.Services.GetRequiredService<UniverseService>();
    var stats = host.Services.GetRequiredService<StatsService>();

    await universe.InitializeAsync(cts.Token);

    await Task.WhenAll(
        universe.RunPeriodicRefreshAsync(cts.Token),
        stats.RunAsync(cts.Token));
}
catch (OperationCanceledException)
{
    // Normal shutdown
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    await Log.CloseAndFlushAsync();
}
