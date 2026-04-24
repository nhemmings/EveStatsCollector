namespace EveStatsCollector.Repositories;

public sealed class UniverseConstellationFilter
{
    private readonly IReadOnlySet<string> _names;

    public bool IsActive => _names.Count > 0;

    public UniverseConstellationFilter(IEnumerable<string> names)
    {
        _names = new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
    }

    public bool AllowConstellation(string name) =>
        !IsActive || _names.Contains(name);
}
