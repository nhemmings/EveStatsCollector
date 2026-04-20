using System.Collections.Concurrent;

namespace EveStatsCollector.Repositories.InMemory;

public abstract class InMemoryRepositoryBase<T, TKey> : IRepository<T, TKey>
    where TKey : notnull
{
    private readonly ConcurrentDictionary<TKey, T> _store = new();
    private readonly Func<T, TKey> _keySelector;

    protected InMemoryRepositoryBase(Func<T, TKey> keySelector) => _keySelector = keySelector;

    public T? GetById(TKey id) => _store.TryGetValue(id, out var entity) ? entity : default;

    public IReadOnlyList<T> GetAll() => _store.Values.ToList();

    public void Upsert(T entity) => _store[_keySelector(entity)] = entity;

    public void UpsertRange(IEnumerable<T> entities)
    {
        foreach (var entity in entities)
            _store[_keySelector(entity)] = entity;
    }

    protected void Clear() => _store.Clear();
}
