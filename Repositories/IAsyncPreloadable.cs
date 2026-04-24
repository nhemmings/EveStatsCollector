namespace EveStatsCollector.Repositories;

internal interface IAsyncPreloadable
{
    Task LoadAsync(CancellationToken ct);
}
