using TrakingTool.Models;

namespace TrakingTool.Services;

public interface IEntryRepository
{
    Task<IReadOnlyList<Entry>> GetAllAsync();
    Task<IReadOnlyList<Entry>> GetByYearAsync(int year);
    Task<Entry?> GetByIdAsync(Guid id);
    Task SaveAsync(Entry entry);
    Task SaveManyAsync(IEnumerable<Entry> entries);
    Task DeleteAsync(Guid id);
}
