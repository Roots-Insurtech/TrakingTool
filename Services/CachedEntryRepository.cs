using Microsoft.AspNetCore.Components.WebAssembly.Authentication;
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
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    // Throttle del background refresh: evita loop "DataChanged → reload pagina → GetAllAsync → refresh → DataChanged".
    private static readonly TimeSpan MinBackgroundRefreshInterval = TimeSpan.FromSeconds(8);
    private DateTimeOffset _lastBackgroundRefresh = DateTimeOffset.MinValue;

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
        // dopo il clear locale qualunque dato remoto è "novità" → forza notify
        _lastBackgroundRefresh = DateTimeOffset.MinValue;
        await RefreshAllFromRemoteAsync(forced: true);
        _state.NotifySynced();
        _state.NotifyDataChanged();
    }

    // ---------- Read (stale-while-revalidate) ----------

    public async Task<IReadOnlyList<Entry>> GetAllAsync()
    {
        var local = await _local.GetAllAsync();
        if (_online.IsOnline && CanStartBackgroundRefresh())
            _ = RefreshAllFromRemoteAsync(forced: false);
        return local;
    }

    public async Task<IReadOnlyList<Entry>> GetByYearAsync(int year)
    {
        var local = await _local.GetByYearAsync(year);
        if (_online.IsOnline && CanStartBackgroundRefresh())
            _ = RefreshYearFromRemoteAsync(year, forced: false);
        return local;
    }

    private bool CanStartBackgroundRefresh()
        => DateTimeOffset.UtcNow - _lastBackgroundRefresh >= MinBackgroundRefreshInterval;

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
            await WithTokenRetry(() => _remote.SaveAsync(entry));
            await _pending.MarkSyncedAsync(entry.Id);
            await UpdatePendingCountAsync();
            _state.NotifySynced();
        }
        catch (AccessTokenNotAvailableException)
        {
            // MSAL non ha ancora il token: l'entry resta pending, riproveremo
            // alla prossima azione/refresh. Niente Error rosso all'utente.
            _state.SetStatus(SyncStatus.Idle);
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
            await WithTokenRetry(() => _remote.DeleteAsync(id));
            await _pending.RemoveDeleteAsync(id);
            await UpdatePendingCountAsync();
            _state.NotifySynced();
        }
        catch (AccessTokenNotAvailableException)
        {
            _state.SetStatus(SyncStatus.Idle);
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
                        await WithTokenRetry(() => _remote.SaveManyAsync(byYear));
                        foreach (var e in byYear)
                            await _pending.MarkSyncedAsync(e.Id);
                    }
                    catch (AccessTokenNotAvailableException)
                    {
                        _state.SetStatus(SyncStatus.Idle);
                        return;
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
                    await WithTokenRetry(() => _remote.DeleteAsync(pd.Id));
                    await _pending.RemoveDeleteAsync(pd.Id);
                }
                catch (AccessTokenNotAvailableException)
                {
                    _state.SetStatus(SyncStatus.Idle);
                    return;
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
            await RefreshAllFromRemoteAsync(forced: true);
        }
        finally
        {
            _drainLock.Release();
        }
    }

    // ---------- Internal: refresh remote → local ----------

    private async Task RefreshAllFromRemoteAsync(bool forced)
    {
        // Solo un refresh alla volta: se ce n'è uno in corso, esci subito.
        if (!await _refreshLock.WaitAsync(0)) return;
        try
        {
            var remote = await WithTokenRetry(() => _remote.GetAllAsync());
            var byYear = remote.GroupBy(e => e.DataRegistrazione.Year);
            var anyChanged = false;
            foreach (var grp in byYear)
            {
                if (await ApplyRemoteToYearAsync(grp.Key, grp.ToList()))
                    anyChanged = true;
            }
            _lastBackgroundRefresh = DateTimeOffset.UtcNow;
            if (anyChanged) _state.NotifyDataChanged();
        }
        catch (AccessTokenNotAvailableException)
        {
            // Token MSAL non ancora pronto (tipico subito dopo login redirect):
            // nessun errore visibile, riproveremo alla prossima sync.
            if (_state.Status != SyncStatus.Offline) _state.SetStatus(SyncStatus.Idle);
        }
        catch (Exception ex)
        {
            _state.SetStatus(SyncStatus.Error, Truncate(ex.Message));
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private async Task RefreshYearFromRemoteAsync(int year, bool forced)
    {
        if (!await _refreshLock.WaitAsync(0)) return;
        try
        {
            var remote = await WithTokenRetry(() => _remote.GetByYearAsync(year));
            var changed = await ApplyRemoteToYearAsync(year, remote.ToList());
            _lastBackgroundRefresh = DateTimeOffset.UtcNow;
            if (changed) _state.NotifyDataChanged();
        }
        catch (AccessTokenNotAvailableException)
        {
            if (_state.Status != SyncStatus.Offline) _state.SetStatus(SyncStatus.Idle);
        }
        catch (Exception ex)
        {
            _state.SetStatus(SyncStatus.Error, Truncate(ex.Message));
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    /// <summary>
    /// Applica gli entry del remoto al year file locale tenendo gli entry pending.
    /// Ritorna true se la cache locale è effettivamente cambiata, false altrimenti
    /// (per evitare DataChanged spuri che ri-triggherebbero le pagine).
    /// </summary>
    private async Task<bool> ApplyRemoteToYearAsync(int year, List<Entry> remoteEntries)
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

        if (AreSameEntries(local, merged))
            return false;

        await _local.ReplaceYearAsync(year, merged);
        return true;
    }

    /// <summary>
    /// Confronto leggero: stesso numero di entry e stessa coppia (Id, LastModified).
    /// Sufficient per evitare DataChanged spuri quando il remoto non ha portato novità.
    /// </summary>
    private static bool AreSameEntries(IReadOnlyList<Entry> a, IReadOnlyList<Entry> b)
    {
        if (a.Count != b.Count) return false;
        var byId = a.ToDictionary(e => e.Id, e => e.LastModified);
        foreach (var e in b)
            if (!byId.TryGetValue(e.Id, out var lm) || lm != e.LastModified)
                return false;
        return true;
    }

    private async Task UpdatePendingCountAsync()
    {
        var count = await _pending.GetTotalPendingCountAsync();
        _state.SetPendingCount(count);
    }

    private static string Truncate(string s, int max = 200)
        => s.Length <= max ? s : s[..max] + "…";

    /// <summary>
    /// Esegue una chiamata Graph che potrebbe fallire con AccessTokenNotAvailableException
    /// se MSAL non ha ancora completato l'acquisizione del token (tipico subito dopo
    /// un login redirect). Riprova una volta dopo 500ms; se anche il retry fallisce,
    /// l'eccezione viene rilanciata e gestita dal chiamante.
    /// </summary>
    private static async Task<T> WithTokenRetry<T>(Func<Task<T>> op)
    {
        try { return await op(); }
        catch (AccessTokenNotAvailableException)
        {
            await Task.Delay(500);
            return await op();
        }
    }

    private static async Task WithTokenRetry(Func<Task> op)
    {
        try { await op(); return; }
        catch (AccessTokenNotAvailableException)
        {
            await Task.Delay(500);
            await op();
        }
    }

    public ValueTask DisposeAsync()
    {
        _online.OnlineChanged -= OnOnlineChanged;
        _drainLock.Dispose();
        _refreshLock.Dispose();
        return ValueTask.CompletedTask;
    }
}
