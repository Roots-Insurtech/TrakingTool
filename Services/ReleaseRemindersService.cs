using Blazored.LocalStorage;
using TrakingTool.Models;

namespace TrakingTool.Services;

/// <summary>
/// Promemoria "Rilasciare Area X" persistenti in localStorage: vengono creati
/// quando un'attività transita allo stato Chiusa e restano visibili finché
/// l'utente non li chiude esplicitamente. Dedup per Cliente+Area attivo.
/// </summary>
public sealed class ReleaseRemindersService
{
    private const string StorageKey = "tt-release-reminders";

    private readonly ILocalStorageService _storage;
    private List<ReleaseReminder> _reminders = new();
    private bool _loaded;

    public ReleaseRemindersService(ILocalStorageService storage) => _storage = storage;

    public event Action? Changed;

    public IReadOnlyList<ReleaseReminder> Reminders => _reminders;

    public async Task EnsureLoadedAsync()
    {
        if (_loaded) return;
        _reminders = await _storage.GetItemAsync<List<ReleaseReminder>>(StorageKey) ?? new();
        _loaded = true;
        Changed?.Invoke();
    }

    public async Task AddAsync(string cliente, string area, Guid entryId)
    {
        await EnsureLoadedAsync();
        if (string.IsNullOrWhiteSpace(area)) return;

        // Dedup per Cliente+Area attivo: una sola "Rilasciare X" alla volta per area.
        if (_reminders.Any(r =>
                string.Equals(r.Cliente, cliente, StringComparison.OrdinalIgnoreCase)
                && string.Equals(r.Area, area, StringComparison.OrdinalIgnoreCase)))
            return;

        _reminders.Add(new ReleaseReminder
        {
            Cliente = cliente ?? string.Empty,
            Area = area,
            EntryId = entryId
        });
        await PersistAsync();
        Changed?.Invoke();
    }

    public async Task DismissAsync(Guid reminderId)
    {
        await EnsureLoadedAsync();
        var removed = _reminders.RemoveAll(r => r.Id == reminderId);
        if (removed == 0) return;
        await PersistAsync();
        Changed?.Invoke();
    }

    /// <summary>
    /// Rimuove eventuali promemoria attivi per la coppia Cliente+Area:
    /// usato quando un'attività esce dallo stato Chiusa (es. l'utente la rilascia
    /// o la annulla) per evitare promemoria orfani.
    /// </summary>
    public async Task DismissByAreaAsync(string cliente, string area)
    {
        await EnsureLoadedAsync();
        if (string.IsNullOrWhiteSpace(area)) return;
        var removed = _reminders.RemoveAll(r =>
            string.Equals(r.Cliente, cliente, StringComparison.OrdinalIgnoreCase)
            && string.Equals(r.Area, area, StringComparison.OrdinalIgnoreCase));
        if (removed == 0) return;
        await PersistAsync();
        Changed?.Invoke();
    }

    private async Task PersistAsync() => await _storage.SetItemAsync(StorageKey, _reminders);
}
