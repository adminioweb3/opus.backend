namespace Citationly.Domain.Entities;

public class AuthProvider
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Provider { get; set; } = string.Empty; // "google", "github", "email"
    public string ProviderUid { get; set; } = string.Empty;
    public DateTime LinkedAt { get; set; }
}

public class PendingAccountLink
{
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public string ProviderUid { get; set; } = string.Empty;
    public string? ProviderEmail { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; }
}
