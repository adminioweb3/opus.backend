using System.Text.Json;
using System.Text.Json.Serialization;
using Citationly.Application.Interfaces;
using Citationly.Application.Interfaces.Prompts;
using Citationly.Domain.Entities;
using Citationly.Infrastructure.Services.Companies;

namespace Citationly.Infrastructure.Services.Prompts;

/// <summary>
/// Exactly 5 topics x 8 prompts (40 total): one fixed "{Business} vs Competitors" topic that
/// legitimately names the business and its real tracked competitors, plus 4 topics named after the
/// business's own real service/technology lines (derived from its onboarding profile, the same way
/// a real AEO tool names them — "Full-stack development", "Generative AI applications" — not generic
/// buyer-journey labels).
///
/// The 4 business-line topics' prompts deliberately do NOT name the business — a prompt like "Is
/// Bee-Glad a worthwhile investment" can only ever mention Bee-Glad, which is what was making every
/// visibility/citation score saturate at 100%: with the business named in the question, a competitor
/// mention was structurally impossible. Keeping these generic is what lets the scoring pipeline
/// (PromptExecutionService/VisibilityCalculatorService) measure real, competitive standing.
/// </summary>
public class PromptDiscoveryService : IPromptDiscoveryService
{
    private readonly IOpenAiService _openAiService;
    private readonly IWebsiteRepository _websiteRepository;

    private const int TopicCount = 4; // business-line topics; the 5th is the fixed comparison topic
    private const int PromptsPerTopic = 8;

    // The model reliably under-delivers on "generate exactly N" (verified empirically elsewhere in
    // this codebase — asked for 20 competitors, got 18). Ask for more than needed and hard-trim in
    // code, so a topic is never short because of model shortfall, and never over because the trim
    // is a hard cap regardless of how many the model returns.
    private const int RequestCount = 10;

    public PromptDiscoveryService(IOpenAiService openAiService, IWebsiteRepository websiteRepository)
    {
        _openAiService = openAiService;
        _websiteRepository = websiteRepository;
    }

    public async Task<List<AiSearchPrompt>> DiscoverPromptsAsync(Guid organizationId, string businessName, string websiteProfile)
    {
        const string systemPrompt = "You are an expert AI Search Prompt Generator. Focus purely on discovery.";
        var ctx = CompanyProfileSummarizer.ExtractContext(websiteProfile);

        var competitorNames = (await _websiteRepository.GetCompetitorsAsync(organizationId))
            .Select(c => c.Name)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Take(5)
            .ToList();

        var topicNames = await DeriveTopicNamesAsync(businessName, ctx, systemPrompt);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var entities = new List<AiSearchPrompt>();

        // Topic 1 (fixed): head-to-head comparison. This is the one topic allowed to name the
        // business directly — that's the entire point of a comparison prompt.
        var comparisonTopic = $"{businessName} vs Competitors";
        var comparisonItems = await DiscoverComparisonBatchAsync(businessName, competitorNames, ctx, systemPrompt);
        AppendTopic(entities, seen, organizationId, comparisonTopic, comparisonItems);

        // Topics 2-5: real business/service lines, generic non-branded prompts.
        var batchTasks = topicNames.Select(name => DiscoverBusinessLineBatchAsync(businessName, name, ctx, systemPrompt));
        var batchResults = await Task.WhenAll(batchTasks);

        for (int i = 0; i < topicNames.Count; i++)
            AppendTopic(entities, seen, organizationId, topicNames[i], batchResults[i]);

        return entities;
    }

    private static void AppendTopic(List<AiSearchPrompt> entities, HashSet<string> seen, Guid organizationId, string topicName, List<DiscoveryPromptItem> items)
    {
        var kept = 0;
        foreach (var p in items)
        {
            if (kept >= PromptsPerTopic) break; // hard cap — never more than 8 for this topic

            var text = (p.prompt ?? "").Trim();
            if (text.Length == 0 || !seen.Add(text)) continue;

            entities.Add(new AiSearchPrompt
            {
                Id = Guid.NewGuid(),
                OrganizationId = organizationId,
                QueryString = text,
                SearchEngine = "Google",
                Topic = topicName,
                IsEnriched = false,
                GeneratedAt = DateTime.UtcNow,
                RawJson = JsonSerializer.Serialize(p, JsonOptions),
            });
            kept++;
        }

        if (kept < PromptsPerTopic)
            Console.WriteLine($"[Discovery] Topic '{topicName}' only yielded {kept}/{PromptsPerTopic} prompts after generation + dedup.");
    }

    /// <summary>
    /// Asks the model to name 4 real service/technology categories from the business's own profile.
    /// Falls back to deriving names directly from the profile's own services/products list if the
    /// call fails or returns too few — never falls back to generic labels, since that's the exact
    /// defect being fixed here.
    /// </summary>
    private async Task<List<string>> DeriveTopicNamesAsync(string businessName, CompanyProfileSummarizer.BiContext ctx, string systemPrompt)
    {
        var userPrompt = $@"Business: {businessName}
Industry: {ctx.Industry}
Core services: {ctx.Services}
Products: {ctx.Products}
Primary technologies: {ctx.Technologies}
Business model: {ctx.BusinessModel}

Name exactly {TopicCount} distinct service, technology, or capability categories that describe what
this business actually does. Each name should be a short, specific, real-sounding category — like
""Full-stack development with modern frameworks and microservices"" or ""Generative AI and AI-powered
applications"" — not a generic marketing-funnel label like ""Pricing"" or ""Comparisons"".

Return a JSON object whose ""topics"" key holds the array of {TopicCount} name strings:
{{""topics"":[""""]}}";

        try
        {
            var response = await _openAiService.GenerateContentAsync(userPrompt, systemPrompt, requireJson: true, model: "gpt-4o-mini");
            var names = ExtractJsonArray<string>(response, "TopicNames", "topics")
                .Select(n => n?.Trim())
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(TopicCount)
                .ToList()!;

            if (names.Count == TopicCount) return names!;
            Console.WriteLine($"[Discovery] Topic naming returned {names.Count}/{TopicCount} usable names; filling from profile.");
            return FillFromProfile(names!, ctx);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Discovery] Topic naming call failed: {ex.Message}; deriving from profile.");
            return FillFromProfile(new List<string>(), ctx);
        }
    }

    /// <summary>Deterministic fallback: pull real category names straight out of the profile's own lists.</summary>
    private static List<string> FillFromProfile(List<string> names, CompanyProfileSummarizer.BiContext ctx)
    {
        var pool = $"{ctx.Services}, {ctx.Products}, {ctx.Technologies}"
            .Split(',')
            .Select(s => s.Trim())
            .Where(s => s.Length > 0 && !string.Equals(s, "Unknown", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase);

        var result = new List<string>(names);
        foreach (var candidate in pool)
        {
            if (result.Count >= TopicCount) break;
            if (!result.Contains(candidate, StringComparer.OrdinalIgnoreCase))
                result.Add(candidate);
        }

        // Still short (a bare-bones or malformed profile) — pad with honest, non-generic labels
        // rather than reintroducing the fixed buyer-journey-stage defect this replaces.
        var padIndex = 0;
        var padding = new[] { "Core Product Capabilities", "Industry Use Cases", "Integrations & Ecosystem", "Customer Success Stories" };
        while (result.Count < TopicCount && padIndex < padding.Length)
        {
            if (!result.Contains(padding[padIndex], StringComparer.OrdinalIgnoreCase))
                result.Add(padding[padIndex]);
            padIndex++;
        }

        return result.Take(TopicCount).ToList();
    }

    private async Task<List<DiscoveryPromptItem>> DiscoverBusinessLineBatchAsync(string businessName, string topicName, CompanyProfileSummarizer.BiContext ctx, string systemPrompt)
    {
        var userPrompt = $@"
Your task is to generate realistic prompts that potential customers would ask AI search engines when
searching for products or services in this specific space — NOT about any single vendor.

## Business context (for grounding only — do not name this business in your output)
Industry: {ctx.Industry}
Target customers: {ctx.TargetAudience}
Business model: {ctx.BusinessModel}

## Objective
Generate {RequestCount} unique, realistic prompts a prospective customer would ask an AI assistant
about: {topicName}

## Instructions
1. Write each prompt exactly as a real, undecided buyer would ask — someone who does NOT yet know
   which company they'll choose.
2. CRITICAL: Do NOT mention '{businessName}' or any other specific company name in the prompt text.
   Ask about the category/capability itself (e.g. ""best tools for X"", ""how do I choose a provider
   for Y"", ""top platforms for Z"") so any real vendor could plausibly be the answer.
3. Every prompt must fit the topic above — do not drift into unrelated question types.
4. Descriptions should sound conversational, the way real users talk to AI assistants.
5. Maximum prompt length: 25 words.
6. Provide ONLY Prompt ID and Prompt text.
7. Output MUST remain below 1000 tokens.
8. Return {RequestCount} distinct prompts. Return exactly the following JSON structure. Do NOT include markdown. Do NOT wrap in ```json.

Return exactly this schema:
{{
  ""prompts"": [
    {{
      ""promptId"": ""PROMPT-001"",
      ""prompt"": """"
    }}
  ]
}}
";
        return await CallDiscoveryAsync(userPrompt, systemPrompt, topicName);
    }

    private async Task<List<DiscoveryPromptItem>> DiscoverComparisonBatchAsync(string businessName, List<string> competitorNames, CompanyProfileSummarizer.BiContext ctx, string systemPrompt)
    {
        var competitorLine = competitorNames.Count > 0
            ? $"Real competitors to reference by name: {string.Join(", ", competitorNames)}."
            : "No specific competitor names are available — phrase these as comparisons against \"alternatives\" or \"other providers\" in the space instead of inventing competitor names.";

        var userPrompt = $@"
Your task is to generate realistic prompts a prospective customer would ask an AI assistant when
directly comparing '{businessName}' to its competition.

## Business context
Industry: {ctx.Industry}
Core services: {ctx.Services}
{competitorLine}

## Objective
Generate {RequestCount} unique, realistic head-to-head comparison prompts. Each one MUST explicitly
mention '{businessName}' by name (e.g. ""{businessName} vs [competitor]"", ""how does {businessName}
compare to [competitor] for X"", ""is {businessName} better than [competitor] for Y"", ""alternatives
to {businessName}""). Do not invent a competitor name that wasn't given above — if none were given,
use generic phrasing like ""alternatives"" instead of a name.

## Instructions
1. Descriptions should sound conversational, the way real users talk to AI assistants.
2. Maximum prompt length: 25 words.
3. Provide ONLY Prompt ID and Prompt text.
4. Output MUST remain below 1000 tokens.
5. Return {RequestCount} distinct prompts. Return exactly the following JSON structure. Do NOT include markdown. Do NOT wrap in ```json.

Return exactly this schema:
{{
  ""prompts"": [
    {{
      ""promptId"": ""PROMPT-001"",
      ""prompt"": """"
    }}
  ]
}}
";
        return await CallDiscoveryAsync(userPrompt, systemPrompt, "vs Competitors");
    }

    private async Task<List<DiscoveryPromptItem>> CallDiscoveryAsync(string userPrompt, string systemPrompt, string logLabel)
    {
        string responseContent;
        try
        {
            responseContent = await _openAiService.GenerateContentAsync(
                prompt: userPrompt,
                systemPrompt: systemPrompt,
                requireJson: true,
                model: "gpt-4o-mini");
        }
        catch (Exception ex)
        {
            // One topic's batch failing shouldn't sink the other four — the org still gets a
            // partial, real prompt set rather than nothing.
            Console.WriteLine($"[Discovery] Batch '{logLabel}' failed: {ex.Message}");
            return new List<DiscoveryPromptItem>();
        }

        return ExtractJsonArray<DiscoveryPromptItem>(responseContent, logLabel, "prompts");
    }

    /// <summary>
    /// OpenAiService sends response_format=json_object, and OpenAI's JSON mode can never return a
    /// bare array — the root is always an object. Pull the array out from under the given key
    /// (falling back to the first array-valued property if the model used a different key), rather
    /// than assuming the model honoured the exact key name asked for.
    /// </summary>
    private static List<T> ExtractJsonArray<T>(string content, string logLabel, string expectedKey)
    {
        var trimmed = StripFences(content);

        try
        {
            using var doc = JsonDocument.Parse(trimmed);
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
                return doc.RootElement.Deserialize<List<T>>(JsonOptions) ?? new();

            if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                if (doc.RootElement.TryGetProperty(expectedKey, out var expected) && expected.ValueKind == JsonValueKind.Array)
                    return expected.Deserialize<List<T>>(JsonOptions) ?? new();

                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    if (prop.Value.ValueKind == JsonValueKind.Array)
                        return prop.Value.Deserialize<List<T>>(JsonOptions) ?? new();
                }
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

    private static string StripFences(string s)
    {
        s = s.Trim();
        if (s.StartsWith("```json"))
        {
            s = s[7..];
            if (s.EndsWith("```")) s = s[..^3];
        }
        if (s.StartsWith("```"))
        {
            s = s[3..];
            if (s.EndsWith("```")) s = s[..^3];
        }
        return s.Trim();
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    private class DiscoveryPromptItem
    {
        public string? promptId { get; set; }
        public string? prompt { get; set; }
    }
}
