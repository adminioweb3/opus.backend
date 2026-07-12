using Citationly.Application.Dtos;

namespace Citationly.Application.Interfaces.GeoDashboard;

public interface IPromptCoverageService
{
    Task<List<PromptTypeCoverageDto>> GetCoverageAsync(Guid organizationId, string range);
}
