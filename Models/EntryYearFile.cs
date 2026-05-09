namespace TrakingTool.Models;

public sealed class EntryYearFile
{
    public int Anno { get; set; }
    public int SchemaVersion { get; set; } = 1;
    public DateTimeOffset UltimaModifica { get; set; } = DateTimeOffset.UtcNow;
    public List<Entry> Entries { get; set; } = new();
}
