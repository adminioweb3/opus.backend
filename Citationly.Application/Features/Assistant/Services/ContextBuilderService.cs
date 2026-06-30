using System.Text.Json;

namespace Citationly.Application.Features.Assistant.Services;

public class ContextBuilderService
{
    public string BuildMergedContext(Dictionary<string, object> analyticsResult, Dictionary<string, object> rawToolData)
    {
        var merged = new Dictionary<string, object>
        {
            { "analytics", analyticsResult },
            { "data", rawToolData }
        };

        return JsonSerializer.Serialize(merged, new JsonSerializerOptions { WriteIndented = true });
    }
}
