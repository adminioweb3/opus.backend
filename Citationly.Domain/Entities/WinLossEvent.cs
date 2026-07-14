namespace Citationly.Domain.Entities;

public class WinLossEvent
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public DateTime Timestamp { get; set; }
    public string Type { get; set; } = string.Empty; // "win" or "loss"
    public string Title { get; set; } = string.Empty;
    public string Engine { get; set; } = string.Empty;
}
