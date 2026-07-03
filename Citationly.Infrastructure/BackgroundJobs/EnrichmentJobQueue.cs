using Citationly.Domain.Entities;

namespace Citationly.Infrastructure.BackgroundJobs;

public class EnrichmentJob
{
    public Guid OrganizationId { get; set; }
    public string ProfileJson { get; set; } = string.Empty;
}
