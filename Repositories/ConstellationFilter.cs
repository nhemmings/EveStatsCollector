namespace EveStatsCollector.Repositories;

public sealed class ConstellationFilter
{
    private readonly IConstellationRepository _constellations;
    private readonly IReadOnlyList<string> _names;
    private readonly Lazy<IReadOnlySet<int>> _systemIds;

    public bool IsActive => _names.Count > 0;

    public ConstellationFilter(IConstellationRepository constellations, IReadOnlyList<string> names)
    {
        _constellations = constellations;
        _names = names;
        _systemIds = new Lazy<IReadOnlySet<int>>(ResolveSystemIds);
    }

    public bool AllowSystem(int systemId) =>
        !IsActive || _systemIds.Value.Contains(systemId);

    private IReadOnlySet<int> ResolveSystemIds()
    {
        var ids = new HashSet<int>();
        foreach (var name in _names)
        {
            var constellation = _constellations.GetAll()
                .FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
            if (constellation is not null)
                foreach (var id in constellation.Systems)
                    ids.Add(id);
        }
        return ids;
    }
}
