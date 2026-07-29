using System.Text.Json;

namespace Citationly.Infrastructure.Services.Companies;

/// <summary>
/// Shared reader over the onboarding business-profile JSON shape (the schema
/// AnalyzeOnboardingCommand produces — every leaf wrapped as {value, confidence}). Used to build
/// both real embedding input text and short candidate summaries for the ranking-only AI prompt,
/// so both Company Knowledge Graph consumers agree on what a "profile" looks like.
/// </summary>
public static class CompanyProfileSummarizer
{
    public record BiContext(string Industry, string Services, string TargetAudience, string BusinessModel, string Products, string Usp, string BrandPositioning, string Technologies, string Scale);

    public static BiContext ExtractContext(string? rawJson)
    {
        string ind = "Unknown", svc = "Unknown", aud = "Unknown", mod = "Unknown",
            prod = "Unknown", usp = "Unknown", brand = "Unknown", tech = "Unknown", scale = "Unknown";
        if (string.IsNullOrEmpty(rawJson)) return new BiContext(ind, svc, aud, mod, prod, usp, brand, tech, scale);

        try
        {
            var doc = JsonDocument.Parse(rawJson);
            var root = doc.RootElement;

            if (root.TryGetProperty("industriesServed", out var iVal) && iVal.TryGetProperty("value", out var iArr) && iArr.ValueKind == JsonValueKind.Array)
                ind = string.Join(", ", iArr.EnumerateArray().Select(x => x.GetString()));

            if (root.TryGetProperty("coreServices", out var sVal) && sVal.TryGetProperty("value", out var sArr) && sArr.ValueKind == JsonValueKind.Array)
                svc = string.Join(", ", sArr.EnumerateArray().Select(x => x.GetString()));

            if (root.TryGetProperty("targetCustomers", out var tVal) && tVal.TryGetProperty("value", out var tArr) && tArr.ValueKind == JsonValueKind.Array)
                aud = string.Join(", ", tArr.EnumerateArray().Select(x => x.GetString()));

            if (root.TryGetProperty("businessModel", out var mVal) && mVal.TryGetProperty("value", out var mStr))
                mod = mStr.GetString() ?? "Unknown";

            if (root.TryGetProperty("products", out var pVal) && pVal.TryGetProperty("value", out var pArr) && pArr.ValueKind == JsonValueKind.Array)
                prod = string.Join(", ", pArr.EnumerateArray().Select(x => x.GetString()));

            if (root.TryGetProperty("uniqueSellingProposition", out var uVal) && uVal.TryGetProperty("value", out var uStr))
                usp = uStr.GetString() ?? "Unknown";

            if (root.TryGetProperty("brandPositioning", out var bVal) && bVal.TryGetProperty("value", out var bStr))
                brand = bStr.GetString() ?? "Unknown";

            if (root.TryGetProperty("primaryTechnologies", out var techVal) && techVal.TryGetProperty("value", out var techArr) && techArr.ValueKind == JsonValueKind.Array)
                tech = string.Join(", ", techArr.EnumerateArray().Select(x => x.GetString()));

            if (root.TryGetProperty("companyScale", out var scaleVal) && scaleVal.TryGetProperty("value", out var scaleStr))
                scale = scaleStr.GetString() ?? "Unknown";
        }
        catch { /* malformed/partial profile JSON — fall through with defaults */ }

        return new BiContext(ind, svc, aud, mod, prod, usp, brand, tech, scale);
    }

    /// <summary>Clean text blob for embedding — real prose, not raw JSON syntax noise.</summary>
    public static string BuildEmbeddingText(string companyName, string? rawProfileJson)
    {
        var ctx = ExtractContext(rawProfileJson);
        return $"Company: {companyName}. Industry: {ctx.Industry}. Services: {ctx.Services}. " +
               $"Products: {ctx.Products}. Target customers: {ctx.TargetAudience}. Business model: {ctx.BusinessModel}. " +
               $"Unique selling proposition: {ctx.Usp}. Brand positioning: {ctx.BrandPositioning}.";
    }

    /// <summary>Short candidate summary for the ranking-only AI prompt.</summary>
    public static string BuildCandidateSummary(Guid id, string companyName, string? industry, string? rawProfileJson)
    {
        var ctx = ExtractContext(rawProfileJson);
        return $"id={id} | name={companyName} | industry={industry ?? ctx.Industry} | services={ctx.Services} | products={ctx.Products} | audience={ctx.TargetAudience}";
    }

    /// <summary>
    /// A company's own real domain-authority estimate from its onboarding analysis, if it has
    /// one — 0 when the company has never been through onboarding's own extraction (thin
    /// candidate). Never invented; this is the same honest neutral default the rest of the app
    /// already uses for ungrounded values.
    /// </summary>
    public static int ExtractDomainAuthorityEstimate(string? rawProfileJson)
    {
        if (string.IsNullOrEmpty(rawProfileJson)) return 0;
        try
        {
            var doc = JsonDocument.Parse(rawProfileJson);
            if (doc.RootElement.TryGetProperty("domainAuthorityEstimate", out var da) &&
                da.TryGetProperty("value", out var val) &&
                val.TryGetProperty("estimatedScore", out var score) &&
                score.ValueKind == JsonValueKind.Number)
            {
                return score.GetInt32();
            }
        }
        catch { /* malformed/partial profile JSON */ }
        return 0;
    }
}
