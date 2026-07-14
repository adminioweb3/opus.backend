using System.Threading.Tasks;
using Citationly.Application.Features.GeoOptimizer;

namespace Citationly.Application.Interfaces.GeoOptimizer;

public interface IGeoOptimizerService
{
    Task<GeoOptimizationResponse> AnalyzeAsync(GeoOptimizationRequest request);
    Task<SchemaGenerationResponse> GenerateSchemaAsync(SchemaGenerationRequest request);
}
