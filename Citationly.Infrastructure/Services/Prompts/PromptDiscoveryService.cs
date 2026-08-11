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
You are an AI visibility research system.

Your job is to generate realistic questions that potential customers would ask
AI assistants when they are trying to discover, evaluate, compare, shortlist,
or hire companies that provide services in a specific market.

These prompts will be used to measure whether a target business is naturally
recommended or mentioned by AI assistants.

## BUSINESS CONTEXT

Industry: {ctx.Industry}
Target Audience: {ctx.TargetAudience}
Business Model: {ctx.BusinessModel}

Topic / Service Category:
{topicName}

Target Business:
{businessName}

IMPORTANT:
The target business is ONLY used as hidden evaluation context.
It must NEVER appear in the generated prompts.

--------------------------------------------------

## PRIMARY OBJECTIVE

Generate {RequestCount} realistic buyer questions that create a genuine
opportunity for AI assistants to recommend companies operating in this category.

The person asking the question:

- has a real business requirement
- is looking for a company, provider, agency, vendor, platform, specialist,
  consultant, or service
- does not already know which company to choose
- wants AI to help discover or evaluate available options

The prompt must therefore create a realistic vendor-discovery situation.

--------------------------------------------------

## CRITICAL BRAND-NEUTRALITY RULES

NEVER mention:

- {businessName}
- the target business
- any specific competitor
- any specific company
- any invented company

Do NOT ask questions such as:

- ""How does X compare with Y?""
- ""Is X the best company?""
- ""What are alternatives to X?""
- ""Why should I choose X?""
- ""What does X offer?""

The user must not know any vendor beforehand.

--------------------------------------------------

## PROMPT TYPES

Generate a natural mixture of these buyer-intent patterns:

1. VENDOR DISCOVERY

Examples:
""Which companies provide professional {topicName} services?""

""Which providers specialize in enterprise {topicName}?""

2. BEST / TOP PROVIDER

Examples:
""What are the best {topicName} companies for startups?""

""Which are the leading companies for {topicName}?""

3. SPECIALIST DISCOVERY

Examples:
""Which companies specialize in {topicName} for {ctx.Industry}?""

""Are there providers experienced in {topicName} for enterprises?""

4. REQUIREMENT-BASED SEARCH

Examples:
""Which {topicName} companies can handle a complex, large-scale project?""

""Who can help with a secure, custom {topicName} implementation?""

5. BUYER EVALUATION

Examples:
""What should I look for when choosing a {topicName} company?""

""How should I evaluate {topicName} providers for an enterprise project?""

6. COMPARISON OF OPTIONS

The comparison must be category-level, NOT brand-level.

Examples:
""How do the top {topicName} companies differ in their services?""

""What factors should I use to compare {topicName} providers?""

7. INDUSTRY-SPECIFIC PROVIDER SEARCH

Examples:
""Which {topicName} companies have experience serving {ctx.TargetAudience}?""

""Which {topicName} providers work with {ctx.Industry} companies?""

8. BUDGET / COMMERCIAL INTENT

Examples:
""What does it typically cost to hire a {topicName} company?""

""Which {topicName} providers offer cost-effective solutions for startups?""

9. TECHNOLOGY / CAPABILITY + PROVIDER

Examples:
""Which companies specialize in advanced {topicName} capabilities?""

""Which providers offer end-to-end {topicName} services?""

10. ALTERNATIVE / SHORTLIST DISCOVERY

Do NOT mention a known company.

Examples:
""What are some good alternatives when choosing a {topicName} provider?""

""Which {topicName} companies should I consider for my shortlist?""

--------------------------------------------------

## IMPORTANT: AVOID LOW-VALUE INFORMATIONAL QUESTIONS

Do NOT generate questions whose answer can be completely satisfied
without mentioning any company or provider — e.g. ""What is {topicName}?"" or
""How does {topicName} work?"". These are educational questions, not
vendor-discovery questions.

--------------------------------------------------

## REALISTIC BUYER BEHAVIOR

Imagine a real buyer who is saying:

""I have this problem. I need someone to solve it.
Which companies should I consider?""

The question should naturally cause an AI assistant to potentially
return a list of companies or service providers.

--------------------------------------------------

## DIVERSITY

Do not generate multiple versions of the same question.

Vary:

- buyer intent
- business problem
- service requirement
- industry
- company size
- technical requirement
- project complexity
- budget
- scalability requirement
- security requirement
- geography when relevant
- evaluation criteria
- buying stage

--------------------------------------------------

## VERY IMPORTANT

The goal is NOT to guarantee that the target business appears.

The goal is to create a neutral market question where the target business
could naturally appear if it has sufficient market visibility.

Do not bias the prompt toward the target business.

--------------------------------------------------

## QUALITY TEST

Before returning each prompt, internally ask:

1. Could a real customer ask this?
2. Does it relate directly to the topic?
3. Is the customer looking for a provider/vendor/company/service?
4. Could multiple real companies plausibly answer it?
5. Does it avoid all company names?
6. Would an AI assistant reasonably recommend companies in its answer?
7. Is it different from the other generated prompts?

Only return prompts that pass all seven checks.

--------------------------------------------------

## OUTPUT

Maximum 25 words per prompt.

Return exactly {RequestCount} distinct prompts.

Return ONLY valid JSON.

Do not include markdown.
Do not include explanations.
Do not include analysis.

Schema:

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
