using Blazored.LocalStorage;
using TrakingTool.Models;

namespace TrakingTool.Services;

/// <summary>
/// Cache locale (LocalStorage), un blob per anno. Sostituirà / sarà affiancato dal sync OneDrive.
/// </summary>
public sealed class LocalEntryRepository : IEntryRepository
{
    private readonly ILocalStorageService _storage;
    private const string KeyPrefix = "tt-entries-";

    public LocalEntryRepository(ILocalStorageService storage) => _storage = storage;

    private static string Key(int year) => $"{KeyPrefix}{year}";

    public async Task<IReadOnlyList<Entry>> GetAllAsync()
    {
        var keys = await _storage.KeysAsync();
        var result = new List<Entry>();
        foreach (var k in keys)
        {
            if (!k.StartsWith(KeyPrefix, StringComparison.Ordinal)) continue;
            var file = await _storage.GetItemAsync<EntryYearFile>(k);
            if (file?.Entries is not null) result.AddRange(file.Entries);
        }
        return result;
    }

    public async Task<IReadOnlyList<Entry>> GetByYearAsync(int year)
    {
        var file = await _storage.GetItemAsync<EntryYearFile>(Key(year));
        return file?.Entries ?? new List<Entry>();
    }

    public async Task<Entry?> GetByIdAsync(Guid id)
    {
        var all = await GetAllAsync();
        return all.FirstOrDefault(e => e.Id == id);
    }

    public async Task SaveAsync(Entry entry)
    {
        var year = entry.DataRegistrazione.Year;
        var file = await _storage.GetItemAsync<EntryYearFile>(Key(year)) ?? new EntryYearFile { Anno = year };
        var idx = file.Entries.FindIndex(e => e.Id == entry.Id);
        if (idx >= 0) file.Entries[idx] = entry;
        else file.Entries.Add(entry);
        file.UltimaModifica = DateTimeOffset.UtcNow;
        await _storage.SetItemAsync(Key(year), file);

        // Se l'entry è stata spostata da un altro anno (idx<0 nel file di destinazione)
        // rimuovi i duplicati negli altri year files. Allinea LocalEntryRepository
        // alla logica già presente in OneDriveEntryRepository.SaveAsync.
        if (idx < 0)
            await RemoveFromOtherYearsAsync(entry.Id, year);
    }

    public async Task SaveManyAsync(IEnumerable<Entry> entries)
    {
        var addedToYear = new List<(Guid id, int year)>();
        foreach (var byYear in entries.GroupBy(e => e.DataRegistrazione.Year))
        {
            var year = byYear.Key;
            var file = await _storage.GetItemAsync<EntryYearFile>(Key(year)) ?? new EntryYearFile { Anno = year };
            foreach (var entry in byYear)
            {
                var idx = file.Entries.FindIndex(e => e.Id == entry.Id);
                if (idx >= 0)
                {
                    file.Entries[idx] = entry;
                }
                else
                {
                    file.Entries.Add(entry);
                    addedToYear.Add((entry.Id, year));
                }
            }
            file.UltimaModifica = DateTimeOffset.UtcNow;
            await _storage.SetItemAsync(Key(year), file);
        }
        foreach (var (id, year) in addedToYear)
            await RemoveFromOtherYearsAsync(id, year);
    }

    private async Task RemoveFromOtherYearsAsync(Guid id, int currentYear)
    {
        var keys = await _storage.KeysAsync();
        var currentKey = Key(currentYear);
        foreach (var k in keys.Where(k => k.StartsWith(KeyPrefix, StringComparison.Ordinal)).ToList())
        {
            if (string.Equals(k, currentKey, StringComparison.Ordinal)) continue;
            var file = await _storage.GetItemAsync<EntryYearFile>(k);
            if (file?.Entries is null) continue;
            var removed = file.Entries.RemoveAll(e => e.Id == id);
            if (removed == 0) continue;
            if (file.Entries.Count == 0)
            {
                await _storage.RemoveItemAsync(k);
            }
            else
            {
                file.UltimaModifica = DateTimeOffset.UtcNow;
                await _storage.SetItemAsync(k, file);
            }
        }
    }

    public async Task DeleteAsync(Guid id)
    {
        var keys = await _storage.KeysAsync();
        foreach (var k in keys)
        {
            if (!k.StartsWith(KeyPrefix, StringComparison.Ordinal)) continue;
            var file = await _storage.GetItemAsync<EntryYearFile>(k);
            if (file?.Entries is null) continue;
            var removed = file.Entries.RemoveAll(e => e.Id == id);
            if (removed > 0)
            {
                file.UltimaModifica = DateTimeOffset.UtcNow;
                await _storage.SetItemAsync(k, file);
            }
        }
    }

    /// <summary>
    /// Sostituisce integralmente il contenuto di un anno (usato dal merge dopo refresh remoto).
    /// </summary>
    public async Task ReplaceYearAsync(int year, IEnumerable<Entry> entries)
    {
        var file = new EntryYearFile
        {
            Anno = year,
            Entries = entries.ToList(),
            UltimaModifica = DateTimeOffset.UtcNow
        };
        await _storage.SetItemAsync(Key(year), file);
    }

    /// <summary>Rimuove tutte le entry cachate (uno per anno) dal LocalStorage.</summary>
    public async Task ClearAsync()
    {
        var keys = await _storage.KeysAsync();
        foreach (var k in keys.Where(k => k.StartsWith(KeyPrefix, StringComparison.Ordinal)).ToList())
            await _storage.RemoveItemAsync(k);
    }
}
