using Blazored.LocalStorage;

namespace TrakingTool.Services;

public sealed record PendingDelete(Guid Id, int Year, DateTimeOffset DeletedAt);

/// <summary>
/// Persiste in LocalStorage le tracce di operazioni offline non ancora propagate a OneDrive:
/// (a) ID di entry create/modificate localmente in attesa di upload;
/// (b) coda di delete pendenti (id + anno) per la propagazione differita.
///
/// Lo stato resta separato dal model Entry per evitare di sporcare i file JSON su OneDrive
/// (la stessa System.Text.Json serializza entrambi i repository).
/// </summary>
public sealed class PendingSyncStore
{
    private const string PendingSyncIdsKey = "tt-pending-sync-ids";
    private const string PendingDeletesKey = "tt-pending-deletes";

    private readonly ILocalStorageService _storage;

    public PendingSyncStore(ILocalStorageService storage) => _storage = storage;

    public async Task<HashSet<Guid>> GetPendingSyncIdsAsync()
    {
        var list = await _storage.GetItemAsync<List<Guid>>(PendingSyncIdsKey);
        return list is null ? new HashSet<Guid>() : new HashSet<Guid>(list);
    }

    public async Task MarkPendingAsync(Guid id)
    {
        var set = await GetPendingSyncIdsAsync();
        if (set.Add(id))
            await _storage.SetItemAsync(PendingSyncIdsKey, set.ToList());
    }

    public async Task MarkSyncedAsync(Guid id)
    {
        var set = await GetPendingSyncIdsAsync();
        if (set.Remove(id))
            await _storage.SetItemAsync(PendingSyncIdsKey, set.ToList());
    }

    public async Task<bool> IsPendingAsync(Guid id)
    {
        var set = await GetPendingSyncIdsAsync();
        return set.Contains(id);
    }

    public async Task<IReadOnlyList<PendingDelete>> GetPendingDeletesAsync()
    {
        return await _storage.GetItemAsync<List<PendingDelete>>(PendingDeletesKey)
            ?? new List<PendingDelete>();
    }

    public async Task EnqueueDeleteAsync(Guid id, int year)
    {
        var list = await _storage.GetItemAsync<List<PendingDelete>>(PendingDeletesKey)
            ?? new List<PendingDelete>();
        if (list.Any(p => p.Id == id)) return;
        list.Add(new PendingDelete(id, year, DateTimeOffset.UtcNow));
        await _storage.SetItemAsync(PendingDeletesKey, list);
    }

    public async Task RemoveDeleteAsync(Guid id)
    {
        var list = await _storage.GetItemAsync<List<PendingDelete>>(PendingDeletesKey);
        if (list is null) return;
        var removed = list.RemoveAll(p => p.Id == id);
        if (removed > 0)
            await _storage.SetItemAsync(PendingDeletesKey, list);
    }

    public async Task<int> GetTotalPendingCountAsync()
    {
        var ids = await GetPendingSyncIdsAsync();
        var deletes = await GetPendingDeletesAsync();
        return ids.Count + deletes.Count;
    }

    /// <summary>Svuota entrambe le code (pending saves + pending deletes).</summary>
    public async Task ClearAsync()
    {
        await _storage.RemoveItemAsync(PendingSyncIdsKey);
        await _storage.RemoveItemAsync(PendingDeletesKey);
    }
}
