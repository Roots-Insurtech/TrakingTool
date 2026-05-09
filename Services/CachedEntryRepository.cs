using TrakingTool.Models;

namespace TrakingTool.Services;

/// <summary>
/// Repository "cache-first" che incapsula <see cref="LocalEntryRepository"/> (LocalStorage)
/// e <see cref="OneDriveEntryRepository"/> (Graph) con strategia stale-while-revalidate
/// in lettura e write-through con coda offline in scrittura.
///
/// Single-user, last-write-wins: le entries pending vincono sempre rispetto al remoto
/// finché non vengono sincronizzate.
/// </summary>
public sealed class CachedEntryRepository : IEntryRepository, IAsyncDisposable
{
    private readonly LocalEntryRepository _local;
    private readonly OneDriveEntryRepository _remote;
    private readonly PendingSyncStore _pending;
    private readonly SyncStateService _state;
    private readonly OnlineStatusService _online;

    private readonly SemaphoreSlim _drainLock = new(1, 1);

    public CachedEntryRepository(
        LocalEntryRepository local,
        OneDriveEntryRepository remote,
        PendingSyncStore pending,
        SyncStateService state,
        OnlineStatusService online)
    {
        _local = local;
        _remote = remote;
        _pending = pending;
        _state = state;
        _online = online;
        _online.OnlineChanged += OnOnlineChanged;
    }

    /// <summary>
    /// Aggancia gli stati e tenta un primo drain se siamo online.
    /// Idempotente: chiamabile da MainLayout su OnAfterRenderAsync.
    /// </summary>
    public async Task InitializeAsync()
    {
        await UpdatePendingCountAsync();
        if (!_online.IsOnline)
        {
            _state.SetStatus(SyncStatus.Offline);
            return;
        }
        _ = DrainAndRefreshAsync();
    }

    private void OnOnlineChanged(bool isOnline)
    {
        if (isOnline) _ = DrainAndRefreshAsync();
        else _state.SetStatus(SyncStatus.Offline);
    }

    /// <summary>Forza un drain + refresh (chiamato dall'icona di sync nell'AppBar).</summary>
    public Task SyncNowAsync() => DrainAndRefreshAsync();

    /// <summary>
    /// Svuota la cache locale e la coda pending, poi ricarica tutto da OneDrive.
    /// Da usare dal pulsante "Pulisci cache" in Settings quando i dati locali divergono o sembrano corrotti.
    /// </summary>
    public async Task ResetLocalAndReloadAsync()
    {
        await _local.ClearAsync();
        await _pending.ClearAsync();
        await UpdatePendingCountAsync();
        await RefreshAllFromRemoteAsync();
        _state.NotifySynced();
        _state.NotifyDataChanged();
    }

    // ---------- Read (stale-while-revalidate) ----------

    public async Task<IReadOnlyList<Entry>> GetAllAsync()
    {
        var local = await _local.GetAllAsync();
        if (_online.IsOnline)
            _ = RefreshAllFromRemoteAsync();
        return local;
    }

    public async Task<IReadOnlyList<Entry>> GetByYearAsync(int year)
    {
        var local = await _local.GetByYearAsync(year);
        if (_online.IsOnline)
            _ = RefreshYearFromRemoteAsync(year);
        return local;
    }

    public Task<Entry?> GetByIdAsync(Guid id) => _local.GetByIdAsync(id);

    // ---------- Write (write-through) ----------

    public async Task SaveAsync(Entry entry)
    {
        entry.LastModified = DateTimeOffset.UtcNow;
        await _local.SaveAsync(entry);
        await _pending.MarkPendingAsync(entry.Id);
        await UpdatePendingCountAsync();
        if (_online.IsOnline) await TryPushSingleAsync(entry);
    }

    public async Task SaveManyAsync(IEnumerable<Entry> entries)
    {
        var list = entries.ToList();
        var now = DateTimeOffset.UtcNow;
        foreach (var e in list) e.LastModified = now;

        await _local.SaveManyAsync(list);
        foreach (var e in list) await _pending.MarkPendingAsync(e.Id);
        await UpdatePendingCountAsync();

        if (_online.IsOnline) _ = DrainAndRefreshAsync();
    }

    public async Task DeleteAsync(Guid id)
    {
        var entry = await _local.GetByIdAsync(id);
        var year = entry?.DataRegistrazione.Year ?? DateTime.UtcNow.Year;
        await _local.DeleteAsync(id);
        // se l'entry era pending sync (mai uploadata) e poi cancellata,
        // basta rimuoverla dalla coda di upload e dal local. Non serve cancellare remote.
        var wasPending = await _pending.IsPendingAsync(id);
        await _pending.MarkSyncedAsync(id);
        if (!wasPending)
            await _pending.EnqueueDeleteAsync(id, year);
        await UpdatePendingCountAsync();

        if (_online.IsOnline && !wasPending)
            await TryPushDeleteAsync(id);
    }

    // ---------- Internal: push singolo / drain ----------

    private async Task TryPushSingleAsync(Entry entry)
    {
        try
        {
            _state.SetStatus(SyncStatus.Syncing);
            await _remote.SaveAsync(entry);
            await _pending.MarkSyncedAsync(entry.Id);
            await UpdatePendingCountAsync();
            _state.NotifySynced();
        }
        catch (Exception ex)
        {
            _state.SetStatus(SyncStatus.Error, Truncate(ex.Message));
        }
    }

    private async Task TryPushDeleteAsync(Guid id)
    {
        try
        {
            _state.SetStatus(SyncStatus.Syncing);
            await _remote.DeleteAsync(id);
            await _pending.RemoveDeleteAsync(id);
            await UpdatePendingCountAsync();
            _state.NotifySynced();
        }
        catch (Exception ex)
        {
            _state.SetStatus(SyncStatus.Error, Truncate(ex.Message));
        }
    }

    private async Task DrainAndRefreshAsync()
    {
        if (!await _drainLock.WaitAsync(0)) return;
        try
        {
            _state.SetStatus(SyncStatus.Syncing);

            // 1) push delle entries pending (saves)
            var pendingIds = await _pending.GetPendingSyncIdsAsync();
            if (pendingIds.Count > 0)
            {
                var allLocal = await _local.GetAllAsync();
                var pendingEntries = allLocal.Where(e => pendingIds.Contains(e.Id)).ToList();
                foreach (var byYear in pendingEntries.GroupBy(e => e.DataRegistrazione.Year))
                {
                    try
                    {
                        await _remote.SaveManyAsync(byYear);
                        foreach (var e in byYear)
                            await _pending.MarkSyncedAsync(e.Id);
                    }
                    catch (Exception ex)
                    {
                        _state.SetStatus(SyncStatus.Error, Truncate(ex.Message));
                        return;
                    }
                }
            }

            // 2) drain delle delete pendenti
            var pendingDeletes = await _pending.GetPendingDeletesAsync();
            foreach (var pd in pendingDeletes)
            {
                try
                {
                    await _remote.DeleteAsync(pd.Id);
                    await _pending.RemoveDeleteAsync(pd.Id);
                }
                catch (Exception ex)
                {
                    _state.SetStatus(SyncStatus.Error, Truncate(ex.Message));
                    return;
                }
            }

            await UpdatePendingCountAsync();
            _state.NotifySynced();

            // 3) refresh dei dati da remote (best-effort, non blocca)
            await RefreshAllFromRemoteAsync();
        }
        finally
        {
            _drainLock.Release();
        }
    }

    // ---------- Internal: refresh remote → local ----------

    private async Task RefreshAllFromRemoteAsync()
    {
        try
        {
            var remote = await _remote.GetAllAsync();
            var byYear = remote.GroupBy(e => e.DataRegistrazione.Year);
            foreach (var grp in byYear)
                await ApplyRemoteToYearAsync(grp.Key, grp.ToList());
            _state.NotifyDataChanged();
        }
        catch (Exception ex)
        {
            _state.SetStatus(SyncStatus.Error, Truncate(ex.Message));
        }
    }

    private async Task RefreshYearFromRemoteAsync(int year)
    {
        try
        {
            var remote = await _remote.GetByYearAsync(year);
            await ApplyRemoteToYearAsync(year, remote.ToList());
            _state.NotifyDataChanged();
        }
        catch (Exception ex)
        {
            _state.SetStatus(SyncStatus.Error, Truncate(ex.Message));
        }
    }

    private async Task ApplyRemoteToYearAsync(int year, List<Entry> remoteEntries)
    {
        var local = await _local.GetByYearAsync(year);
        var pendingIds = await _pending.GetPendingSyncIdsAsync();
        var pendingDeletes = (await _pending.GetPendingDeletesAsync()).Select(d => d.Id).ToHashSet();

        var merged = new List<Entry>();
        var taken = new HashSet<Guid>();

        // Le entries locali pending vincono sempre (versione più recente non ancora pushata)
        foreach (var le in local)
        {
            if (pendingIds.Contains(le.Id))
            {
                merged.Add(le);
                taken.Add(le.Id);
            }
        }

        // Aggiungi entries remote escludendo quelle già viste o in coda di delete
        foreach (var re in remoteEntries)
        {
            if (taken.Contains(re.Id)) continue;
            if (pendingDeletes.Contains(re.Id)) continue;
            if (re.LastModified == DateTimeOffset.MinValue)
                re.LastModified = DateTimeOffset.UtcNow; // backfill per legacy entries
            merged.Add(re);
        }

        await _local.ReplaceYearAsync(year, merged);
    }

    private async Task UpdatePendingCountAsync()
    {
        var count = await _pending.GetTotalPendingCountAsync();
        _state.SetPendingCount(count);
    }

    private static string Truncate(string s, int max = 200)
        => s.Length <= max ? s : s[..max] + "…";

    public ValueTask DisposeAsync()
    {
        _online.OnlineChanged -= OnOnlineChanged;
        _drainLock.Dispose();
        return ValueTask.CompletedTask;
    }
}
