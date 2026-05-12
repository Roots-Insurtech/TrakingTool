using Microsoft.AspNetCore.Components;
using MudBlazor;
using TrakingTool.Models;
using TrakingTool.Pages;

namespace TrakingTool.Services;

/// <summary>
/// Apre il dialog di creazione/modifica di un Entry, persiste il risultato
/// tramite il repository e mostra lo snackbar coerente con lo stato di sync.
/// Centralizza la logica condivisa tra Home, Entries e Recap.
/// </summary>
public sealed class EntryEditor
{
    private readonly IDialogService _dialog;
    private readonly ISnackbar _snackbar;
    private readonly IEntryRepository _repo;
    private readonly SyncStateService _sync;
    private readonly ReleaseRemindersService _reminders;

    public EntryEditor(IDialogService dialog, ISnackbar snackbar, IEntryRepository repo, SyncStateService sync, ReleaseRemindersService reminders)
    {
        _dialog = dialog;
        _snackbar = snackbar;
        _repo = repo;
        _sync = sync;
        _reminders = reminders;
    }

    /// <summary>Apre il dialog su una nuova attività. Ritorna true se è stata salvata.</summary>
    public Task<bool> CreateAsync(IEnumerable<Entry> allEntries) =>
        ShowAsync(new Entry { DataRegistrazione = DateOnly.FromDateTime(DateTime.Today) }, isNew: true, allEntries, originalStato: null);

    /// <summary>Apre il dialog su una copia dell'attività indicata. Ritorna true se è stata salvata.</summary>
    public Task<bool> EditAsync(Entry entry, IEnumerable<Entry> allEntries) =>
        ShowAsync(Copy(entry), isNew: false, allEntries, originalStato: entry.Stato);

    private async Task<bool> ShowAsync(Entry entry, bool isNew, IEnumerable<Entry> allEntries, Stato? originalStato)
    {
        var list = allEntries as IReadOnlyList<Entry> ?? allEntries.ToList();
        var clienti = list.Select(e => e.Cliente).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().OrderBy(s => s).ToArray();
        var aree = list.Select(e => e.Area).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().OrderBy(s => s).ToArray();

        var parameters = new DialogParameters
        {
            ["Entry"] = entry,
            ["IsNew"] = isNew,
            ["ClientiSuggeriti"] = clienti,
            ["AreeSuggerite"] = aree
        };
        var options = new DialogOptions { CloseButton = true, MaxWidth = MaxWidth.Small, FullWidth = true };
        var dlg = await _dialog.ShowAsync<EntryDialog>(isNew ? "Nuova attività" : "Modifica attività", parameters, options);
        var result = await dlg.Result;
        if (result is null || result.Canceled || result.Data is not Entry saved) return false;

        await _repo.SaveAsync(saved);
        await SyncRemindersAsync(saved, originalStato);
        _snackbar.Add(_sync.GetSaveResultMessage(isNew ? "Attività creata" : "Modifiche salvate"), SeverityFromState());
        return true;
    }

    private async Task SyncRemindersAsync(Entry saved, Stato? originalStato)
    {
        // Transizione → Chiusa: crea promemoria di rilascio.
        if (saved.Stato == Stato.Chiusa && originalStato != Stato.Chiusa)
        {
            await _reminders.AddAsync(saved.Cliente, saved.Area, saved.Id);
            return;
        }
        // Transizione Chiusa → altro stato: rimuovi eventuale promemoria attivo
        // sulla stessa area (l'utente ha già rilasciato/annullato/riaperto).
        if (originalStato == Stato.Chiusa && saved.Stato != Stato.Chiusa)
        {
            await _reminders.DismissByAreaAsync(saved.Cliente, saved.Area);
        }
    }

    private Severity SeverityFromState() => _sync.Status switch
    {
        SyncStatus.Offline => Severity.Info,
        SyncStatus.Error => Severity.Warning,
        _ => Severity.Success
    };

    private static Entry Copy(Entry e) => new()
    {
        Id = e.Id,
        Cliente = e.Cliente,
        Area = e.Area,
        Stato = e.Stato,
        Descrizione = e.Descrizione,
        DataRegistrazione = e.DataRegistrazione,
        DataRilascio = e.DataRilascio,
        LastModified = e.LastModified
    };
}
