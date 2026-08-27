using System.Threading.Tasks;
using Citationly.Application.Features.GeoOptimizer;

namespace Citationly.Application.Interfaces.GeoOptimizer;

public interface IGeoOptimizerService
{
    Task<GeoOptimizationResponse> AnalyzeAsync(Guid organizationId, GeoOptimizationRequest request);
    Task<SchemaGenerationResponse> GenerateSchemaAsync(Guid organizationId, SchemaGenerationRequest request);
}
