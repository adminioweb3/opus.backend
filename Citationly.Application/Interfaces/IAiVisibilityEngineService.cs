namespace Citationly.Application.Interfaces;

public interface IAiVisibilityEngineService
{
    Task RunAnalysisAsync(Guid organizationId);
}
