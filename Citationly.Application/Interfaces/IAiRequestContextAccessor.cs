namespace Citationly.Application.Interfaces;

public interface IAiRequestContextAccessor
{
    Guid? OrganizationId { get; set; }
}
