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
}
