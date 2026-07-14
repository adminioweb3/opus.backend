using Citationly.Application.Dtos;

namespace Citationly.Application.Interfaces.GeoDashboard;

public interface IGeoPillarService
{
    Task<List<GeoPillarDto>> GetPillarsAsync(Guid organizationId, string range);
}
