namespace TrakingTool.Services;

public enum SyncStatus
{
    Idle,      // Tutto sincronizzato, online
    Syncing,   // Operazione di sync in corso
    Offline,   // Browser segnala offline o ultime chiamate Graph fallite
    Error      // Errore durante l'ultima sync (stato online ignoto)
}

/// <summary>
/// Stato globale della sincronizzazione esposto alla UI.
/// </summary>
public sealed class SyncStateService
{
    public SyncStatus Status { get; private set; } = SyncStatus.Idle;
    public int PendingCount { get; private set; }
    public DateTimeOffset? LastSyncedAt { get; private set; }
    public string? LastError { get; private set; }

    /// <summary>Triggered quando cambia stato/pending/lastSync/error.</summary>
    public event Action? StateChanged;

    /// <summary>Triggered quando la cache locale è stata aggiornata da remoto e la UI dovrebbe ri-leggere.</summary>
    public event Action? DataChanged;

    public void SetStatus(SyncStatus status, string? error = null)
    {
        if (Status == status && LastError == error) return;
        Status = status;
        LastError = error;
        StateChanged?.Invoke();
    }

    public void SetPendingCount(int count)
    {
        if (PendingCount == count) return;
        PendingCount = count;
        StateChanged?.Invoke();
    }

    public void NotifySynced()
    {
        LastSyncedAt = DateTimeOffset.UtcNow;
        if (Status == SyncStatus.Syncing) Status = SyncStatus.Idle;
        LastError = null;
        StateChanged?.Invoke();
    }

    public void NotifyDataChanged() => DataChanged?.Invoke();
}
