namespace Citationly.Application.Interfaces.GeoDashboard;

public interface IEngineScanService
{
    Task<(int EnginesScanned, int PromptsTracked)> GetScanStatsAsync(Guid organizationId);
}
