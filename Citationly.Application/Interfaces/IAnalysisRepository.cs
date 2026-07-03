using System;
using System.Threading.Tasks;
using Citationly.Domain.Entities;

namespace Citationly.Application.Interfaces
{
    public interface IAnalysisRepository
    {
        Task<Guid> CreateAnalysisRunAsync(AnalysisRun run);
        Task UpdateAnalysisRunAsync(AnalysisRun run);
        
        Task<Guid> CreateDashboardSnapshotAsync(DashboardSnapshot snapshot);
        Task<DashboardSnapshot?> GetLatestDashboardSnapshotAsync(Guid organizationId);
        
        Task AddVisibilityHistoryAsync(VisibilityHistory history);
        Task AddCitationHistoryAsync(CitationHistory history);
        Task AddRecommendationHistoryAsync(RecommendationHistory history);
        Task AddPromptHistoryAsync(PromptHistory history);
    }
}
