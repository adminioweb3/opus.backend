namespace Citationly.Domain.Entities;

public class PromptCoverage
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public DateOnly ScanDate { get; set; }
    public string PromptType { get; set; } = string.Empty;
    public string Example { get; set; } = string.Empty;
    public string Note { get; set; } = string.Empty;
    public int Percentage { get; set; }
    public string Direction { get; set; } = string.Empty;
}
