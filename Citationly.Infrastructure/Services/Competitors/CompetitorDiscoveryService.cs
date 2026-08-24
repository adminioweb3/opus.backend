using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Citationly.Application.Interfaces;
using Citationly.Application.Interfaces.Companies;
using Citationly.Application.Interfaces.Competitors;
using Citationly.Domain.Entities;
using Citationly.Domain.Utils;
using Citationly.Infrastructure.Services.Companies;

namespace Citationly.Infrastructure.Services.Competitors;

/// <summary>
/// Hybrid competitor discovery. Real companies already in the Company Knowledge Graph come first:
/// candidates are found by cosine similarity over real embeddings, gated on a minimum similarity so
/// a loosely-related company can never pass as a competitor, then ranked/explained by one AI call
/// that may only pick from the ids it was handed. Their similarity number always comes straight
/// from cosine similarity, so it can't be re-inflated the way the old additive scoring engine used
/// to saturate at 100.
///
/// The graph rarely holds 20 close matches early in its life, so whatever the graph is short by is
/// topped up with AI-generated real companies rather than returning a short list — a client seeing
/// 3 competitors reads as a broken report. Generated entries always rank below every real match and
/// carry a synthetic similarity, and each one is persisted as a (thin) graph node so the graph
/// grows with use.
/// </summary>
public class CompetitorDiscoveryService : ICompetitorDiscoveryService
{
    private const int CandidatePoolSize = 100;
    private const int TopSelectionCount = 40;

    /// <summary>
    /// Minimum cosine similarity for a graph company to count as a real competitor. Without a floor,
    /// a single unrelated company in the graph was enough to satisfy "candidates exist", which
    /// suppressed generation entirely and made that one junk row the whole competitor list.
    ///
    /// This value is a judgement call, not a natural constant — business descriptions share enough
    /// vocabulary ("platform", "services", "customers") that unrelated companies still score
    /// mid-range. Every rejected candidate is logged with its actual score; calibrate from those.
    /// </summary>
    private const double MinCosineSimilarity = 0.70;

    /// <summary>Top of the synthetic similarity band used for generated entries, and the step between them.</summary>
    private const decimal GeneratedTopSimilarity = 80m;
    private const decimal SimilarityStep = 0.5m;

    /// <summary>
    /// The model under-delivers on "exactly N" — asked for 20, it returns 18 — and the code-side
    /// dedup and self-checks can drop more on top of that. The scale filter below adds a third
    /// source of drop-out (a famous giant offered despite instructions gets rejected outright, not
    /// just discouraged), so this needs more headroom than a plain dedup pass would.
    /// </summary>
    private const int GenerationHeadroom = 15;

    private static readonly string[] ScaleTiers = { "startup", "smb", "mid-market", "enterprise" };

    /// <summary>
    /// 0-3, or -1 if unrecognized/unknown. Matches loosely (contains) since the model doesn't always
    /// echo the exact casing/hyphenation asked for.
    /// </summary>
    private static int ScaleTierIndex(string? scale)
    {
        if (string.IsNullOrWhiteSpace(scale)) return -1;
        var normalized = scale.Trim().ToLowerInvariant();
        for (int i = 0; i < ScaleTiers.Length; i++)
            if (normalized.Contains(ScaleTiers[i])) return i;
        return -1;
    }

    /// <summary>Minimum times a name must appear in real AI responses before it's promoted to an
    /// observed competitor - filters out one-off mentions that aren't a real pattern.</summary>
    private const int MinObservedMentionCount = 2;

    /// <summary>How far back to look for observed co-occurrence.</summary>
    private const int ObservedLookbackDays = 90;

    private readonly ICompanySimilarityService _similarityService;
    private readonly IOpenAiService _openAiService;
    private readonly ICompanyRepository _companyRepository;
    private readonly IPromptIntelligenceRepository _promptIntelligenceRepository;

    public CompetitorDiscoveryService(
        ICompanySimilarityService similarityService,
        IOpenAiService openAiService,
        ICompanyRepository companyRepository,
        IPromptIntelligenceRepository promptIntelligenceRepository)
    {
        _similarityService = similarityService;
        _openAiService = openAiService;
        _companyRepository = companyRepository;
        _promptIntelligenceRepository = promptIntelligenceRepository;
    }

    public async Task<List<CompanyCompetitor>> DiscoverCompetitorsAsync(
        Guid organizationId,
        Guid companyId,
        string businessName,
        string rawProfileJson,
        CancellationToken cancellationToken)
    {
        var pool = await _similarityService.GetTopSimilarAsync(companyId, CandidatePoolSize);
        var candidates = pool.Where(c => c.CosineSimilarity >= MinCosineSimilarity).ToList();
        LogThresholdOutcome(pool, candidates.Count);

        var graphEdges = candidates.Count > 0
            ? await RankGraphCandidatesAsync(companyId, businessName, rawProfileJson, candidates)
            : new List<CompanyCompetitor>();
        foreach (var edge in graphEdges) edge.DiscoverySource = "graph";

        List<CompanyCompetitor> combined;
        if (graphEdges.Count >= TopSelectionCount)
        {
            combined = graphEdges.Take(TopSelectionCount).ToList();
        }
        else
        {
            var shortfall = TopSelectionCount - graphEdges.Count;
            Console.WriteLine($"[Discovery] Graph supplied {graphEdges.Count}/{TopSelectionCount}; generating {shortfall} to top up.");

            var usedIds = graphEdges.Select(e => e.CompetitorCompanyId).ToHashSet();
            var excludeDomains = candidates
                .Where(c => usedIds.Contains(c.Company.Id))
                .Select(c => c.Company.NormalizedDomain)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            // Generated entries rank below every real match, so their synthetic similarity has to sit
            // below the weakest real score too — otherwise the table shows a generated row at 80%
            // ranked underneath a real row at 72%.
            var startSimilarity = graphEdges.Count > 0
                ? Math.Min(graphEdges.Min(e => e.Similarity) - SimilarityStep, GeneratedTopSimilarity)
                : GeneratedTopSimilarity;

            var generated = await GenerateCompetitorsAsync(
                companyId,
                businessName,
                rawProfileJson,
                count: shortfall,
                startRank: graphEdges.Count + 1,
                startSimilarity: startSimilarity,
                excludeDomains: excludeDomains,
                cancellationToken);
            foreach (var edge in generated) edge.DiscoverySource = "generated";

            combined = graphEdges.Concat(generated).ToList();
            Console.WriteLine($"[Discovery] Returning {combined.Count} competitors ({graphEdges.Count} from graph, {generated.Count} generated).");
        }

        return await PromoteObservedCompetitorsAsync(organizationId, companyId, businessName, combined, cancellationToken);
    }

    /// <summary>
    /// Phase 3 C4: a company that repeatedly appears alongside the brand in real AI responses
    /// (PromptMentions, populated from actual captured LLM output - not a guess) is stronger
    /// evidence of real competition than either embedding similarity or an LLM's suggestion. This
    /// only promotes names that already resolve to an existing Company Knowledge Graph node by
    /// exact name match - it does not invent a company or guess a domain for a bare mention.
    /// </summary>
    private async Task<List<CompanyCompetitor>> PromoteObservedCompetitorsAsync(
        Guid organizationId,
        Guid companyId,
        string businessName,
        List<CompanyCompetitor> combined,
        CancellationToken cancellationToken)
    {
        IEnumerable<CompetitorMentionSummaryRow> mentionRows;
        try
        {
            mentionRows = await _promptIntelligenceRepository.GetCompetitorMentionSummaryDataAsync(
                organizationId, DateTime.UtcNow.AddDays(-ObservedLookbackDays));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Discovery] Observed-mention lookup failed, skipping promotion: {ex.Message}");
            return combined;
        }

        var mentionCounts = mentionRows
            .GroupBy(m => m.CompetitorName.Trim(), StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() >= MinObservedMentionCount
                        && !string.Equals(g.Key, businessName, StringComparison.OrdinalIgnoreCase))
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

        if (mentionCounts.Count == 0) return combined;

        var existingByCompanyId = combined.ToDictionary(e => e.CompetitorCompanyId);
        var newlyObserved = new List<CompanyCompetitor>();

        foreach (var (name, count) in mentionCounts.OrderByDescending(kv => kv.Value))
        {
            Company? matched;
            try
            {
                matched = await _companyRepository.FindByNameAsync(name);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Discovery] FindByNameAsync failed for '{name}': {ex.Message}");
                continue;
            }

            if (matched == null || matched.Id == companyId) continue;

            var evidenceNote = $"Co-occurs with your brand in {count} real AI responses over the last {ObservedLookbackDays} days.";

            if (existingByCompanyId.TryGetValue(matched.Id, out var existingEdge))
            {
                // Already surfaced via graph/generation - upgrade its badge and reason rather than
                // duplicating the row.
                existingEdge.DiscoverySource = "observed";
                existingEdge.Reason = string.IsNullOrWhiteSpace(existingEdge.Reason)
                    ? evidenceNote
                    : $"{evidenceNote} {existingEdge.Reason}";
            }
            else
            {
                newlyObserved.Add(new CompanyCompetitor
                {
                    CompanyId = companyId,
                    CompetitorCompanyId = matched.Id,
                    Similarity = 0, // not a cosine-similarity match - the evidence here is observed co-occurrence, not embedding distance
                    Confidence = Math.Min(100, count * 20), // derived from real mention frequency, not an AI guess
                    Reason = evidenceNote,
                    Strength = null,
                    Weakness = null,
                    DiscoverySource = "observed",
                });
            }
        }

        if (newlyObserved.Count == 0) return combined;

        // Observed evidence outranks everything else - new observed entries go to the front, then
        // the rest keep their relative order. Trim from the tail (weakest generated/graph entries)
        // if this pushes the list past the cap, never trimming an observed entry.
        var reordered = newlyObserved.Concat(combined).Take(TopSelectionCount).ToList();
        for (int i = 0; i < reordered.Count; i++) reordered[i].Rank = i + 1;

        Console.WriteLine($"[Discovery] Promoted {newlyObserved.Count} new observed competitor(s) from real AI-response co-occurrence.");
        return reordered;
    }

    /// <summary>
    /// Logs how the threshold split the pool, and every rejected candidate's actual score, so
    /// MinCosineSimilarity can be calibrated against real data instead of guessed at.
    /// </summary>
    private static void LogThresholdOutcome(List<(Company Company, double CosineSimilarity)> pool, int passedCount)
    {
        Console.WriteLine($"[Discovery] Graph pool: {pool.Count} embedded companies, {passedCount} at or above {MinCosineSimilarity:P0}.");
        foreach (var (company, score) in pool.Where(c => c.CosineSimilarity < MinCosineSimilarity).Take(10))
            Console.WriteLine($"[Discovery]   rejected {company.CompanyName} ({company.NormalizedDomain}) at {score:P1}");
    }

    private async Task<List<CompanyCompetitor>> RankGraphCandidatesAsync(
        Guid companyId,
        string businessName,
        string rawProfileJson,
        List<(Company Company, double CosineSimilarity)> candidates)
    {
        var candidatesById = candidates.ToDictionary(c => c.Company.Id, c => c);

        var selections = await SelectAndExplainAsync(businessName, rawProfileJson, candidates);

        // Enforce "never invent" in code, not just in the prompt — drop anything the model
        // returned that isn't literally one of the ids we handed it.
        var validSelections = selections
            .Where(s => candidatesById.ContainsKey(s.CompanyId))
            .DistinctBy(s => s.CompanyId)
            .Take(TopSelectionCount)
            .ToList();

        // AI call failed or returned nothing usable — fall back to the candidates that already
        // passed the threshold, ordered by cosine, with no AI-written reason.
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
                Confidence = NormalizeConfidence(s.Confidence),
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

        try
        {
            using var doc = JsonDocument.Parse(trimmed);

            if (doc.RootElement.ValueKind == JsonValueKind.Array)
                return DeserializeElements<T>(doc.RootElement, logLabel);

            if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    if (prop.Value.ValueKind == JsonValueKind.Array)
                        return DeserializeElements<T>(prop.Value, logLabel);
                }

                // No array anywhere: the model emitted {"c1":{...},"c2":{...}}. Take the
                // object-valued properties as the items.
                var fromValues = new List<T>();
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    if (prop.Value.ValueKind != JsonValueKind.Object) continue;
                    var item = TryDeserialize<T>(prop.Value, logLabel);
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

    /// <summary>
    /// Deserializes array entries one at a time. Doing the whole array in a single call meant one
    /// malformed field dropped every entry — a confidence of "85" instead of 85 silently cost the
    /// ranking path all 20 of its selections and degraded it to raw cosine order.
    /// </summary>
    private static List<T> DeserializeElements<T>(JsonElement array, string logLabel)
    {
        var items = new List<T>();
        foreach (var element in array.EnumerateArray())
        {
            var item = TryDeserialize<T>(element, logLabel);
            if (item != null) items.Add(item);
        }
        return items;
    }

    private static T? TryDeserialize<T>(JsonElement element, string logLabel)
    {
        try
        {
            return element.Deserialize<T>(LenientJson);
        }
        catch (JsonException ex)
        {
            Console.WriteLine($"[Discovery] {logLabel}: skipped malformed entry: {ex.Message}");
            return default;
        }
    }

    private static readonly JsonSerializerOptions LenientJson = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        Converters = { new TolerantIntConverter() }
    };

    /// <summary>
    /// The model formats numeric fields loosely — 85, "85" and 85.0 have all come back for
    /// confidence. Coerce any of those to an int rather than failing the entry.
    /// </summary>
    private sealed class TolerantIntConverter : JsonConverter<int>
    {
        public override int Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.Number:
                    return reader.TryGetInt32(out var i) ? i : (int)Math.Round(reader.GetDouble());
                case JsonTokenType.String:
                    var raw = reader.GetString();
                    if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                        return parsed;
                    if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
                        return (int)Math.Round(d);
                    return 0;
                case JsonTokenType.Null:
                    return 0;
                default:
                    reader.Skip();
                    return 0;
            }
        }

        public override void Write(Utf8JsonWriter writer, int value, JsonSerializerOptions options)
            => writer.WriteNumberValue(value);
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
Company scale: {ctx.Scale}
Industry: {ctx.Industry}
Services: {ctx.Services}
Products: {ctx.Products}
Target customers: {ctx.TargetAudience}
Business model: {ctx.BusinessModel}
Unique selling proposition: {ctx.Usp}

CANDIDATES (id | name | industry | services | products | audience):
{candidateLines}

Select up to {TopSelectionCount} candidates that are genuine, FAIR competitors to the Business above —
same business model, comparable to its stated company scale ({ctx.Scale}), serving the same kind of
customer. Order them most-to-least relevant. Leave out any candidate that is merely adjacent rather
than competing, or that is a broad category-dominating giant clearly larger than this business's own
scale; returning fewer is correct and expected. For each, explain why in a business-relevant way.
Reason/strength/weakness: max 20 words each. confidence: whole number between 0 and 100 (not a fraction).

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

        /// <summary>Read as double because the model answers on either scale — see NormalizeConfidence.</summary>
        public double Confidence { get; set; }

        public string? Reason { get; set; }
        public string? Strength { get; set; }
        public string? Weakness { get; set; }
    }

    /// <summary>
    /// The model answers confidence on whichever scale it feels like: the generation prompt pins a
    /// 60-90 range and gets 0-100 back, while the ranking prompt returned 0.95 for "very confident",
    /// which landed in the UI as 1%. Anything at or below 1 is a fraction.
    /// </summary>
    private static int NormalizeConfidence(double raw)
    {
        var scaled = raw > 0 && raw <= 1.0 ? raw * 100 : raw;
        return (int)Math.Round(Math.Clamp(scaled, 0, 100));
    }

    /// <summary>
    /// Fills the gap between what the graph could supply and TopSelectionCount. Each generated
    /// company is upserted as a graph node (thin — no profile or embedding until that company is
    /// itself analyzed), so repeat generations across orgs converge on the same rows.
    /// </summary>
    private async Task<List<CompanyCompetitor>> GenerateCompetitorsAsync(
        Guid companyId,
        string businessName,
        string rawProfileJson,
        int count,
        int startRank,
        decimal startSimilarity,
        HashSet<string> excludeDomains,
        CancellationToken cancellationToken)
    {
        var ctx = CompanyProfileSummarizer.ExtractContext(rawProfileJson);

        const string systemPrompt =
            "You are a competitive intelligence analyst. Name only real, existing companies with real " +
            "websites — never invent a company. Output a single JSON object, nothing else.";

        var exclusions = excludeDomains.Count > 0
            ? $"\nAlready covered, do NOT repeat: {string.Join(", ", excludeDomains)}"
            : string.Empty;

        // "well-known companies" (the old wording) reliably pulled category-dominating giants —
        // Microsoft, Google, NVIDIA — into every industry's competitor list regardless of the
        // target business's actual scale, which reads as an unfair, meaningless comparison for a
        // small or niche business. Ask for peer-level competitors instead, with explicit negative
        // examples, the same pattern already used elsewhere in this file to steer the model away
        // from a default bias (see the "do not name the business" prompt in PromptDiscoveryService).
        // Also require a per-competitor scale tag — this is what lets ValidateScale below reject a
        // mismatch in code rather than trusting the model's own restraint, which prompt wording
        // alone can't guarantee.
        var userPrompt = $@"Business: {businessName}
Company scale: {ctx.Scale}
Industry: {ctx.Industry}
Services: {ctx.Services}
Products: {ctx.Products}
Target customers: {ctx.TargetAudience}
Business model: {ctx.BusinessModel}{exclusions}

List {count + GenerationHeadroom} real companies that are FAIR, comparable competitors to this
business — similar in scale, maturity, and market position, actually competing for the same
customers. This business's own scale is {ctx.Scale}: do NOT default to broad category-dominating
giants (e.g. Microsoft, Google, Amazon, NVIDIA, Salesforce) unless that genuinely matches — a
startup or SMB should be compared against other startups/SMBs in its specific niche, not against
unrelated market leaders just because they're in the same broad industry.
Every entry must be a company that actually exists, with its real website domain. Do not include
the business itself.
reason/strength/weakness: max 15 words each. confidence: 60-90.
scale: your own best-guess estimate of that COMPETITOR's size — exactly one of ""Startup"", ""SMB"",
""Mid-Market"", ""Enterprise"".

Return a JSON object whose ""competitors"" key holds the array:
{{""competitors"":[{{""name"":""Example Inc"",""website"":""example.com"",""reason"":"""",""strength"":"""",""weakness"":"""",""confidence"":75,""scale"":""SMB""}}]}}";

        List<ColdStartCompetitor> generated;
        try
        {
            Console.WriteLine($"[Discovery] Asking AI for {count + GenerationHeadroom} competitors (need {count})...");
            var responseContent = await _openAiService.GenerateContentAsync(
                userPrompt, systemPrompt, requireJson: true, model: "gpt-4o-mini");

            Console.WriteLine($"[Discovery] AI raw response: {responseContent[..Math.Min(300, responseContent.Length)]}");
            generated = ExtractJsonArray<ColdStartCompetitor>(responseContent, "Generation");
            Console.WriteLine($"[Discovery] AI returned {generated.Count} competitors");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Discovery] Generation call failed: {ex.Message}");
            return new List<CompanyCompetitor>();
        }

        var edges = new List<CompanyCompetitor>();
        var seen = new HashSet<string>(excludeDomains, StringComparer.OrdinalIgnoreCase);

        var businessTier = ScaleTierIndex(ctx.Scale);

        foreach (var competitor in generated)
        {
            if (edges.Count >= count) break;

            if (string.IsNullOrWhiteSpace(competitor.Name) || string.IsNullOrWhiteSpace(competitor.Website))
            {
                Console.WriteLine("[Discovery] Skipping entry with empty name or website");
                continue;
            }

            // Enforced in code, not just requested in the prompt — a famous giant offered despite
            // the instructions gets rejected here regardless of how the model justified it. Only
            // filters when both scales are known, and only rejects when the competitor is MORE than
            // one tier above the business — an SMB seeing a mid-market competitor is still a
            // reasonable stretch goal; an SMB seeing an "Enterprise" giant is the exact mismatch
            // that prompted this whole feature.
            var competitorTier = ScaleTierIndex(competitor.Scale);
            if (businessTier >= 0 && competitorTier >= 0 && competitorTier - businessTier > 1)
            {
                Console.WriteLine($"[Discovery] Skipping {competitor.Name}: scale '{competitor.Scale}' too far above business scale '{ctx.Scale}'");
                continue;
            }

            var normalizedDomain = DomainNormalizer.Normalize(competitor.Website);

            // Enforce the exclusion list in code, not just in the prompt — same reason the ranking
            // path re-checks ids: the model does not reliably honour it.
            if (!seen.Add(normalizedDomain))
            {
                Console.WriteLine($"[Discovery] Skipping {competitor.Name}: {normalizedDomain} already covered");
                continue;
            }

            try
            {
                var upserted = await _companyRepository.UpsertAsync(new Company
                {
                    NormalizedDomain = normalizedDomain,
                    Website = competitor.Website,
                    CompanyName = competitor.Name,
                    Industry = ctx.Industry,
                    BusinessProfileJson = "{}",   // thin node until this company is itself analyzed
                    SourceOrganizationId = null,  // generated, not contributed by an org
                    LastAnalyzedAt = DateTime.UtcNow
                });

                // chk_companycompetitor_not_self would reject this, and it would be wrong anyway —
                // the model occasionally returns the business itself.
                if (upserted.Id == companyId)
                {
                    Console.WriteLine($"[Discovery] Skipping {competitor.Name}: resolves to the business itself");
                    continue;
                }

                edges.Add(new CompanyCompetitor
                {
                    CompanyId = companyId,
                    CompetitorCompanyId = upserted.Id,
                    Similarity = Math.Max(startSimilarity - (edges.Count * SimilarityStep), 0m),
                    Confidence = Math.Clamp(competitor.Confidence, 60, 100),
                    Rank = startRank + edges.Count,
                    Reason = competitor.Reason,
                    Strength = competitor.Strength,
                    Weakness = competitor.Weakness
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Discovery] Failed to persist {competitor.Name}: {ex.Message}");
            }
        }

        Console.WriteLine($"[Discovery] Generated {edges.Count} of {count} requested");
        return edges;
    }

    private class ColdStartCompetitor
    {
        public string? Name { get; set; }
        public string? Website { get; set; }
        public string? Reason { get; set; }
        public string? Strength { get; set; }
        public string? Weakness { get; set; }
        public int Confidence { get; set; }
        public string? Scale { get; set; }
    }
}
