namespace Citationly.Domain.Entities;

public class Subscription
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public string? StripeSubscriptionId { get; set; }
    public string PlanKey { get; set; } = "Trial";
    public string Status { get; set; } = "trialing";
    public DateTime? CurrentPeriodStart { get; set; }
    public DateTime? CurrentPeriodEnd { get; set; }
    public bool CancelAtPeriodEnd { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class Invoice
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public string? StripeInvoiceId { get; set; }
    public long AmountDueCents { get; set; }
    public long AmountPaidCents { get; set; }
    public string Currency { get; set; } = "usd";
    public string Status { get; set; } = "draft";
    public string? HostedInvoiceUrl { get; set; }
    public DateTime? IssuedAt { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class PaymentMethod
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public string? StripePaymentMethodId { get; set; }
    public string? Brand { get; set; }
    public string? Last4 { get; set; }
    public int? ExpMonth { get; set; }
    public int? ExpYear { get; set; }
    public bool IsDefault { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class UsageCounter
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public string MetricKey { get; set; } = string.Empty;
    public DateTime PeriodStart { get; set; }
    public DateTime PeriodEnd { get; set; }
    public long Count { get; set; }
    public DateTime UpdatedAt { get; set; }
}
