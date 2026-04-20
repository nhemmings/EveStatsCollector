namespace EveStatsCollector.Repositories;

public interface IRepository<T, TKey> where TKey : notnull
{
    T? GetById(TKey id);
    IReadOnlyList<T> GetAll();
    void Upsert(T entity);
    void UpsertRange(IEnumerable<T> entities);
}
