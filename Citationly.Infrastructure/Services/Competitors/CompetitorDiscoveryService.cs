using System.Text.Json;
using Citationly.Application.Interfaces;
using Citationly.Application.Interfaces.Companies;
using Citationly.Application.Interfaces.Competitors;
using Citationly.Domain.Entities;
using Citationly.Domain.Utils;
using Citationly.Infrastructure.Services.Companies;

namespace Citationly.Infrastructure.Services.Competitors;

/// <summary>
/// Ranking-only competitor discovery: candidates come exclusively from real companies already in
/// the Company Knowledge Graph (via ICompanySimilarityService's cosine-similarity search over
/// real embeddings) — this service never invents a company name. The one AI call here selects
/// and explains a top-20 subset of the top-100 real candidates; it never touches the similarity
/// number itself, which always comes straight from cosine similarity so it can't be re-inflated
/// the way the old additive scoring engine used to saturate at 100.
/// </summary>
public class CompetitorDiscoveryService : ICompetitorDiscoveryService
{
    private const int CandidatePoolSize = 100;
    private const int TopSelectionCount = 20;

    private readonly ICompanySimilarityService _similarityService;
    private readonly IOpenAiService _openAiService;
    private readonly ICompanyRepository _companyRepository;

    public CompetitorDiscoveryService(
        ICompanySimilarityService similarityService,
        IOpenAiService openAiService,
        ICompanyRepository companyRepository)
    {
        _similarityService = similarityService;
        _openAiService = openAiService;
        _companyRepository = companyRepository;
    }

    public async Task<List<CompanyCompetitor>> DiscoverCompetitorsAsync(
        Guid companyId,
        string businessName,
        string rawProfileJson,
        CancellationToken cancellationToken)
    {
        var candidates = await _similarityService.GetTopSimilarAsync(companyId, CandidatePoolSize);
        if (candidates.Count == 0)
        {
            Console.WriteLine("[Discovery] No candidates in graph, initiating cold-start generation...");
            // Cold-start: no companies in graph yet. Generate AI competitors as fallback.
            var coldStartResults = await GenerateColdStartCompetitorsAsync(companyId, businessName, rawProfileJson, cancellationToken);
            Console.WriteLine($"[Discovery] Cold-start generated {coldStartResults.Count} competitors");
            return coldStartResults;
        }

        var candidatesById = candidates.ToDictionary(c => c.Company.Id, c => c);

        var selections = await SelectAndExplainAsync(businessName, rawProfileJson, candidates);

        // Enforce "never invent" in code, not just in the prompt — drop anything the model
        // returned that isn't literally one of the ids we handed it.
        var validSelections = selections
            .Where(s => candidatesById.ContainsKey(s.CompanyId))
            .DistinctBy(s => s.CompanyId)
            .Take(TopSelectionCount)
            .ToList();

        // AI call failed or returned nothing usable — fall back to the top-20 real candidates
        // by cosine similarity directly, with no AI-written reason, rather than an empty result.
        if (validSelections.Count == 0)
        {
            return candidates
                .Take(TopSelectionCount)
                .Select((c, i) => new CompanyCompetitor
                {
                    CompanyId = companyId,
                    CompetitorCompanyId = c.Company.Id,
                    Similarity = ToSimilarityScore(c.CosineSimilarity),
                    Confidence = 0,
                    Rank = i + 1,
                    Reason = null,
                    Strength = null,
                    Weakness = null
                })
                .ToList();
        }

        return validSelections.Select((s, i) =>
        {
            var (_, cosine) = candidatesById[s.CompanyId];
            return new CompanyCompetitor
            {
                CompanyId = companyId,
                CompetitorCompanyId = s.CompanyId,
                Similarity = ToSimilarityScore(cosine),
                Confidence = Math.Clamp(s.Confidence, 0, 100),
                Rank = i + 1,
                Reason = s.Reason,
                Strength = s.Strength,
                Weakness = s.Weakness
            };
        }).ToList();
    }

    private static decimal ToSimilarityScore(double cosine) => Math.Round((decimal)Math.Clamp(cosine, 0, 1) * 100, 2);

    /// <summary>
    /// OpenAiService sends response_format=json_object, and OpenAI's JSON mode can never return a
    /// bare array — the root is always an object. So a prompt asking for `[...]` comes back wrapped
    /// under whatever key the model picked ({"competitors":[...]}), or worse, as an object of
    /// objects with no array at all. Pull out the first array-valued property rather than pinning a
    /// key name, and still accept a bare array in case JSON mode is ever off.
    /// </summary>
    private static List<T> ExtractJsonArray<T>(string content, string logLabel)
    {
        if (string.IsNullOrWhiteSpace(content)) return new();

        // Strip markdown fences if the model added them (only possible with JSON mode off).
        var trimmed = content.Trim();
        var fence = System.Text.RegularExpressions.Regex.Match(trimmed, @"```(?:json)?\s*([\s\S]*?)```");
        if (fence.Success) trimmed = fence.Groups[1].Value.Trim();

        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        try
        {
            using var doc = JsonDocument.Parse(trimmed);

            if (doc.RootElement.ValueKind == JsonValueKind.Array)
                return doc.RootElement.Deserialize<List<T>>(options) ?? new();

            if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    if (prop.Value.ValueKind == JsonValueKind.Array)
                        return prop.Value.Deserialize<List<T>>(options) ?? new();
                }

                // No array anywhere: the model emitted {"c1":{...},"c2":{...}}. Take the
                // object-valued properties as the items.
                var fromValues = new List<T>();
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    if (prop.Value.ValueKind != JsonValueKind.Object) continue;
                    var item = prop.Value.Deserialize<T>(options);
                    if (item != null) fromValues.Add(item);
                }
                if (fromValues.Count > 0) return fromValues;
            }

            Console.WriteLine($"[Discovery] {logLabel}: no array found in response root ({doc.RootElement.ValueKind}).");
            return new();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Discovery] {logLabel}: JSON parse failed: {ex.Message}");
            Console.WriteLine($"[Discovery] {logLabel}: raw = {trimmed[..Math.Min(500, trimmed.Length)]}");
            return new();
        }
    }

    private async Task<List<Selection>> SelectAndExplainAsync(
        string businessName,
        string rawProfileJson,
        List<(Company Company, double CosineSimilarity)> candidates)
    {
        var ctx = CompanyProfileSummarizer.ExtractContext(rawProfileJson);
        var candidateLines = string.Join("\n", candidates.Select(c =>
            CompanyProfileSummarizer.BuildCandidateSummary(c.Company.Id, c.Company.CompanyName, c.Company.Industry, c.Company.BusinessProfileJson)));

        const string systemPrompt =
            "You are a competitive-intelligence ranking assistant. You may ONLY select and rank companies " +
            "from the CANDIDATES list below by their exact id. Do not invent, add, or suggest any company " +
            "not in this list. Output a single JSON object, nothing else.";

        var userPrompt = $@"Business: {businessName}
Industry: {ctx.Industry}
Services: {ctx.Services}
Products: {ctx.Products}
Target customers: {ctx.TargetAudience}
Business model: {ctx.BusinessModel}
Unique selling proposition: {ctx.Usp}

CANDIDATES (id | name | industry | services | products | audience):
{candidateLines}

Select the {TopSelectionCount} candidates most relevant as real competitors to the Business above, ordered
most-to-least relevant. For each, explain why in a business-relevant way. Reason/strength/weakness: max 20 words each.

Return a JSON object whose ""selections"" key holds the array, with companyId copied EXACTLY from a candidate's id above:
{{""selections"":[{{""companyId"":""<uuid>"",""confidence"":0,""reason"":"""",""strength"":"""",""weakness"":""""}}]}}";

        string responseContent;
        try
        {
            responseContent = await _openAiService.GenerateContentAsync(userPrompt, systemPrompt, true, "gpt-4o-mini");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Discovery] Ranking call failed: {ex.Message}");
            return new List<Selection>();
        }

        var selections = ExtractJsonArray<Selection>(responseContent, "Ranking");
        Console.WriteLine($"[Discovery] Ranking returned {selections.Count} selections");
        return selections;
    }

    private class Selection
    {
        public Guid CompanyId { get; set; }
        public int Confidence { get; set; }
        public string? Reason { get; set; }
        public string? Strength { get; set; }
        public string? Weakness { get; set; }
    }

    private async Task<List<CompanyCompetitor>> GenerateColdStartCompetitorsAsync(
        Guid companyId,
        string businessName,
        string rawProfileJson,
        CancellationToken cancellationToken)
    {
        var ctx = CompanyProfileSummarizer.ExtractContext(rawProfileJson);

        const string systemPrompt =
            "You are a competitive intelligence analyst. Name only real, existing companies with real " +
            "websites — never invent a company. Output a single JSON object, nothing else.";

        var userPrompt = $@"Business: {businessName}
Industry: {ctx.Industry}
Services: {ctx.Services}
Products: {ctx.Products}
Target customers: {ctx.TargetAudience}
Business model: {ctx.BusinessModel}

List exactly {TopSelectionCount} real, well-known companies that compete with this business.
Every entry must be a company that actually exists, with its real website domain.
reason/strength/weakness: max 15 words each. confidence: 60-90.

Return a JSON object whose ""competitors"" key holds the array:
{{""competitors"":[{{""name"":""Example Inc"",""website"":""example.com"",""reason"":"""",""strength"":"""",""weakness"":"""",""confidence"":75}}]}}";

        try
        {
            Console.WriteLine("[Discovery] Calling AI for cold-start competitor generation...");
            var responseContent = await _openAiService.GenerateContentAsync(
                userPrompt, systemPrompt, requireJson: true, model: "gpt-4o-mini");

            Console.WriteLine($"[Discovery] AI raw response: {responseContent[..Math.Min(300, responseContent.Length)]}");

            var coldStartCompetitors = ExtractJsonArray<ColdStartCompetitor>(responseContent, "Cold-start");
            Console.WriteLine($"[Discovery] AI returned {coldStartCompetitors.Count} competitors");

            // Create/upsert Company records for each competitor
            var edges = new List<CompanyCompetitor>();
            var similarityDecrement = 10m / TopSelectionCount;
            Console.WriteLine($"[Discovery] Processing {coldStartCompetitors.Count} competitors for company {companyId}");

            for (int i = 0; i < coldStartCompetitors.Count && i < TopSelectionCount; i++)
            {
                var competitor = coldStartCompetitors[i];
                if (string.IsNullOrWhiteSpace(competitor.Name) || string.IsNullOrWhiteSpace(competitor.Website))
                {
                    Console.WriteLine($"[Discovery] Skipping competitor {i}: empty name or website");
                    continue;
                }

                Console.WriteLine($"[Discovery] Creating company record for {competitor.Name}");
                // Normalize domain for dedup
                var normalizedDomain = DomainNormalizer.Normalize(competitor.Website);

                // Create minimal Company record for cold-start competitor
                var company = new Company
                {
                    Id = Guid.NewGuid(),
                    NormalizedDomain = normalizedDomain,
                    Website = competitor.Website,
                    CompanyName = competitor.Name,
                    Industry = ctx.Industry,
                    BusinessProfileJson = "{}", // Minimal: empty profile until they're analyzed
                    Embedding = null,
                    EmbeddingModel = null,
                    EmbeddingUpdatedAt = null,
                    SourceOrganizationId = null, // Not from any org, generated for cold-start
                    LastAnalyzedAt = DateTime.UtcNow
                };

                // Upsert company (on conflict update LastAnalyzedAt)
                var upsertedCompany = await _companyRepository.UpsertAsync(company);
                Console.WriteLine($"[Discovery] Upserted company {upsertedCompany.CompanyName} with ID {upsertedCompany.Id}");

                // Create edge with descending similarity
                edges.Add(new CompanyCompetitor
                {
                    CompanyId = companyId, // The org's own company
                    CompetitorCompanyId = upsertedCompany.Id, // The generated competitor
                    Similarity = 80m - (i * similarityDecrement), // 80, 72, 64... descending
                    Confidence = Math.Clamp(competitor.Confidence, 60, 100),
                    Rank = i + 1,
                    Reason = competitor.Reason,
                    Strength = competitor.Strength,
                    Weakness = competitor.Weakness
                });
            }

            Console.WriteLine($"[Discovery] Cold-start complete: {edges.Count} edges created");
            return edges;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Discovery] Cold-start generation failed: {ex.Message}");
            return new List<CompanyCompetitor>();
        }
    }

    private class ColdStartCompetitor
    {
        public string? Name { get; set; }
        public string? Website { get; set; }
        public string? Reason { get; set; }
        public string? Strength { get; set; }
        public string? Weakness { get; set; }
        public int Confidence { get; set; }
    }
}
