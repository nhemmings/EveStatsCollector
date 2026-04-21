using EveStatsCollector.Esi;
using EveStatsCollector.Repositories;
using EveStatsCollector.Repositories.InMemory;
using EveStatsCollector.Services;
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

            services.AddSingleton<ISolarSystemRepository, InMemorySolarSystemRepository>();
            services.AddSingleton<IConstellationRepository, InMemoryConstellationRepository>();
            services.AddSingleton<IRegionRepository, InMemoryRegionRepository>();
            services.AddSingleton<IKillsReportRepository, InMemoryKillsReportRepository>();
            services.AddSingleton<IJumpsReportRepository, InMemoryJumpsReportRepository>();
            services.AddConstellationFilter(ctx.Configuration); // comment out to disable

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
