namespace TrakingTool.Models;

public sealed class ReleaseReminder
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid EntryId { get; set; }
    public string Cliente { get; set; } = string.Empty;
    public string Area { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
