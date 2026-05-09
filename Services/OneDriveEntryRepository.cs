using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using TrakingTool.Models;

namespace TrakingTool.Services;

/// <summary>
/// Repository che persiste su OneDrive (cartella applicazione dedicata via Files.ReadWrite.AppFolder).
/// Un file JSON per anno: entries-{anno}.json sotto /me/drive/special/approot.
/// </summary>
public sealed class OneDriveEntryRepository : IEntryRepository
{
    private const string ApprootChildren = "me/drive/special/approot/children?$select=name,size,lastModifiedDateTime";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly JsonSerializerOptions GraphOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly IHttpClientFactory _httpFactory;

    public OneDriveEntryRepository(IHttpClientFactory httpFactory) => _httpFactory = httpFactory;

    private HttpClient Client() => _httpFactory.CreateClient("graph");

    private static string FileName(int year) => $"entries-{year}.json";
    private static string ContentPath(int year) => $"me/drive/special/approot:/{FileName(year)}:/content";
    private static string ItemPath(int year) => $"me/drive/special/approot:/{FileName(year)}";

    public async Task<IReadOnlyList<Entry>> GetByYearAsync(int year)
    {
        var resp = await Client().GetAsync(ContentPath(year));
        if (resp.StatusCode == HttpStatusCode.NotFound) return Array.Empty<Entry>();
        resp.EnsureSuccessStatusCode();
        await using var stream = await resp.Content.ReadAsStreamAsync();
        var file = await JsonSerializer.DeserializeAsync<EntryYearFile>(stream, JsonOpts);
        return file?.Entries ?? new List<Entry>();
    }

    public async Task<IReadOnlyList<Entry>> GetAllAsync()
    {
        var listing = await Client().GetFromJsonAsync<DriveChildrenResponse>(ApprootChildren, GraphOpts);
        var years = (listing?.Value ?? new())
            .Select(item => ParseYear(item.Name))
            .Where(y => y.HasValue)
            .Select(y => y!.Value)
            .Distinct()
            .OrderBy(y => y)
            .ToList();

        var all = new List<Entry>();
        foreach (var y in years)
            all.AddRange(await GetByYearAsync(y));
        return all;
    }

    public async Task<Entry?> GetByIdAsync(Guid id)
    {
        var all = await GetAllAsync();
        return all.FirstOrDefault(e => e.Id == id);
    }

    public async Task SaveAsync(Entry entry)
    {
        var year = entry.DataRegistrazione.Year;
        var entries = (await GetByYearAsync(year)).ToList();
        var idx = entries.FindIndex(e => e.Id == entry.Id);
        if (idx >= 0) entries[idx] = entry;
        else entries.Add(entry);

        var file = new EntryYearFile
        {
            Anno = year,
            Entries = entries,
            UltimaModifica = DateTimeOffset.UtcNow
        };
        await UploadAsync(year, file);

        if (idx < 0)
        {
            // l'entry potrebbe essere stata spostata da un anno all'altro: pulisci dagli altri anni
            var all = await GetAllAsync();
            var other = all.Where(e => e.Id == entry.Id && e.DataRegistrazione.Year != year).ToList();
            foreach (var stale in other)
                await DeleteFromYearAsync(stale.Id, stale.DataRegistrazione.Year);
        }
    }

    public async Task SaveManyAsync(IEnumerable<Entry> entries)
    {
        foreach (var byYear in entries.GroupBy(e => e.DataRegistrazione.Year))
        {
            var year = byYear.Key;
            var existing = (await GetByYearAsync(year)).ToList();
            foreach (var entry in byYear)
            {
                var idx = existing.FindIndex(e => e.Id == entry.Id);
                if (idx >= 0) existing[idx] = entry;
                else existing.Add(entry);
            }
            var file = new EntryYearFile
            {
                Anno = year,
                Entries = existing,
                UltimaModifica = DateTimeOffset.UtcNow
            };
            await UploadAsync(year, file);
        }
    }

    public async Task DeleteAsync(Guid id)
    {
        var all = await GetAllAsync();
        foreach (var year in all.Where(e => e.Id == id).Select(e => e.DataRegistrazione.Year).Distinct())
            await DeleteFromYearAsync(id, year);
    }

    private async Task DeleteFromYearAsync(Guid id, int year)
    {
        var entries = (await GetByYearAsync(year)).ToList();
        var removed = entries.RemoveAll(e => e.Id == id);
        if (removed == 0) return;

        if (entries.Count == 0)
        {
            // file vuoto → rimuoviamo l'item su OneDrive
            var del = await Client().DeleteAsync(ItemPath(year));
            if (del.StatusCode != HttpStatusCode.NotFound) del.EnsureSuccessStatusCode();
        }
        else
        {
            var file = new EntryYearFile
            {
                Anno = year,
                Entries = entries,
                UltimaModifica = DateTimeOffset.UtcNow
            };
            await UploadAsync(year, file);
        }
    }

    private async Task UploadAsync(int year, EntryYearFile file)
    {
        var json = JsonSerializer.Serialize(file, JsonOpts);
        using var content = new StringContent(json);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        var resp = await Client().PutAsync(ContentPath(year), content);
        resp.EnsureSuccessStatusCode();
    }

    private static int? ParseYear(string? name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        const string prefix = "entries-";
        const string suffix = ".json";
        if (!name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
            !name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) return null;
        var core = name.Substring(prefix.Length, name.Length - prefix.Length - suffix.Length);
        return int.TryParse(core, out var y) ? y : null;
    }

    private sealed record DriveChildrenResponse(List<DriveItem>? Value);
    private sealed record DriveItem(string? Name, long? Size, DateTimeOffset? LastModifiedDateTime);
}
