using System.Text.Json.Serialization;

namespace TrakingTool.Models;

public sealed class Entry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Cliente { get; set; } = string.Empty;
    public string Area { get; set; } = string.Empty;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public Stato Stato { get; set; } = Stato.Aperta;

    public string Descrizione { get; set; } = string.Empty;
    public DateOnly DataRegistrazione { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    public DateOnly? DataRilascio { get; set; }

    // Last user modification time, used for last-write-wins sync between
    // OneDrive and the local cache. MinValue means "legacy entry without
    // timestamp" — the cached repository migrates these on first encounter.
    public DateTimeOffset LastModified { get; set; } = DateTimeOffset.MinValue;
}
