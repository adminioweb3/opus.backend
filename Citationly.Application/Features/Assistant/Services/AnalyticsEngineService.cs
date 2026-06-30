namespace Citationly.Application.Features.Assistant.Services;

public class AnalyticsEngineService
{
    public Dictionary<string, object> RunCalculations(Dictionary<string, object> rawToolData)
    {
        var analytics = new Dictionary<string, object>();

        // Example calculation: Average Visibility
        if (rawToolData.ContainsKey("websites") && rawToolData["websites"] is IEnumerable<dynamic> websites)
        {
            var webList = websites.ToList();
            if (webList.Any())
            {
                double sum = 0;
                int count = 0;
                foreach(var w in webList)
                {
                    if (w.VisibilityScore != null)
                    {
                        sum += (double)w.VisibilityScore;
                        count++;
                    }
                }
                if (count > 0)
                {
                    analytics["averageVisibility"] = Math.Round(sum / count, 2);
                }
            }
        }

        // We can do percentiles, growth, median here outside of LLM

        return analytics;
    }
}
