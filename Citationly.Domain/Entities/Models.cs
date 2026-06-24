namespace Citationly.Domain.Entities;

public class Organization
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

public class User
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public string FirebaseUid { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public string Role { get; set; } = "Viewer";
    public DateTime CreatedAt { get; set; }
}

public class Website
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public string DomainUrl { get; set; } = string.Empty;
    public string PlatformName { get; set; } = "Custom";
    public int HealthScore { get; set; } = 0;
    public int VisibilityScore { get; set; } = 0;
    public string Status { get; set; } = "Connected";
    public DateTime? LastSyncAt { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class CrawledPage
{
    public Guid Id { get; set; }
    public Guid WebsiteId { get; set; }
    public string Url { get; set; } = string.Empty;
    public string? Content { get; set; }
    public string? Title { get; set; }
    public DateTime LastCrawledAt { get; set; }
}

public class Recommendation
{
    public Guid Id { get; set; }
    public Guid WebsiteId { get; set; }
    public Guid CrawledPageId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? ActionType { get; set; }
    public string? Priority { get; set; }
    public string Status { get; set; } = "Pending";
    public DateTime CreatedAt { get; set; }
}

public class Integration
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public string PlatformName { get; set; } = string.Empty;
    public string? ApiUrl { get; set; }
    public string? ApiKey { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class Embedding
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid ReferenceId { get; set; }
    public string ReferenceType { get; set; } = string.Empty;
    public string TextContent { get; set; } = string.Empty;
    public double[] Vector { get; set; } = Array.Empty<double>();
    public DateTime CreatedAt { get; set; }
}

public class HistoricalScan
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public DateTime ScanDate { get; set; }
    public int VisibilityScore { get; set; }
    public int CitationScore { get; set; }
    public int SentimentScore { get; set; }
    public int CompetitorScore { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class ShareOfVoice
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public DateTime ScanDate { get; set; }
    public string CompetitorName { get; set; } = string.Empty;
    public int SharePercentage { get; set; }
    public string ColorCode { get; set; } = "#000000";
    public DateTime CreatedAt { get; set; }
}
