namespace Citationly.Domain.Entities;

public class GeoPillar
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public DateOnly ScanDate { get; set; }
    public string PillarKey { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int Score { get; set; }
}
