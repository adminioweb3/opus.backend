using System;
using System.Text.Json;
using System.Threading.Tasks;
using Citationly.Application.Features.AnswerSimulator;
using Citationly.Application.Interfaces;
using Citationly.Application.Interfaces.AnswerSimulator;

namespace Citationly.Infrastructure.Services.AnswerSimulator;

public class AnswerSimulatorService : IAnswerSimulatorService
{
    private readonly IOpenAiService _openAiService;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public AnswerSimulatorService(IOpenAiService openAiService)
    {
        _openAiService = openAiService;
    }

    public async Task<SimulateAnswerResponse> SimulateAsync(SimulateAnswerRequest request)
    {
        var brand = string.IsNullOrWhiteSpace(request.Brand) ? "the brand" : request.Brand;

        var answerSystemPrompt =
            $"You are an AI search assistant answering for {request.Persona}, who is currently {request.Stage}, " +
            $"based in {request.Region}. Give a concise, well-structured answer (about 120-170 words) naming " +
            "specific real products, brands, or sources where relevant, as a real AI search engine would for this person. " +
            "Do not mention that you are an AI or reference this being a simulation.";

        var answer = await _openAiService.GenerateContentAsync(request.Prompt, answerSystemPrompt);

        var analysisSystemPrompt =
            "You analyze an AI-generated answer for brand visibility. Return ONLY a JSON object with EXACTLY these keys: " +
            "\"mentioned\" (bool — does the answer reference the given brand, even indirectly), " +
            "\"position\" (string, e.g. \"1st of 4\" if ranked among named options, or \"Not mentioned\"), " +
            "\"sentiment\" (one of \"pos\", \"neu\", \"neg\"), " +
            "\"sharePct\" (integer 0-100, estimated share of the answer's attention/space the brand receives), " +
            "\"competitors\" (array of {\"name\":string,\"sharePct\":integer 0-100} for other named brands/products), " +
            "\"sources\" (array of {\"name\":string,\"type\":\"you\"|\"comp\"|\"third\"} for anything the answer implies as a source or reference), " +
            "\"summary\" (one short sentence on how the brand comes across).";

        var analysisUserPrompt = $"Brand to check for: {brand}\n\nAI answer to analyze:\n{answer}";

        SimulateAnswerResponse result;
        try
        {
            var raw = await _openAiService.GenerateContentAsync(analysisUserPrompt, analysisSystemPrompt, requireJson: true);
            result = JsonSerializer.Deserialize<SimulateAnswerResponse>(raw, JsonOptions) ?? new SimulateAnswerResponse();
        }
        catch (Exception)
        {
            var mentioned = answer.Contains(brand, StringComparison.OrdinalIgnoreCase);
            result = new SimulateAnswerResponse
            {
                Mentioned = mentioned,
                Position = mentioned ? "Mentioned" : "Not mentioned",
                Sentiment = "neu",
                SharePct = mentioned ? 40 : 5,
                Summary = mentioned
                    ? $"{brand} is referenced in the answer."
                    : $"{brand} does not appear in this answer."
            };
        }

        result.Answer = answer;
        result.ConsistencyOutOfFive = EstimateConsistency(result.Mentioned, result.SharePct);
        return result;
    }

    public async Task<CompareContentResponse> CompareAsync(CompareContentRequest request)
    {
        var brand = string.IsNullOrWhiteSpace(request.Brand) ? "the brand" : request.Brand;
        var pageContent = request.PageContent.Length > 3000 ? request.PageContent[..3000] : request.PageContent;

        var systemPrompt =
            "You simulate how an AI search engine's answer to a question would differ depending on whether it had " +
            "access to a specific page of content. Return ONLY a JSON object with EXACTLY these keys: " +
            "\"without\" (string, 60-90 words — a plausible AI answer with NO awareness of the provided page content), " +
            "\"with\" (string, 60-90 words — a plausible AI answer that DOES cite/reflect the provided page content), " +
            "\"changed\" (bool — whether the answer meaningfully changes), " +
            "\"verdict\" (one short sentence on the impact of having this content indexed).";

        var userPrompt = $"Question: {request.Prompt}\nBrand: {brand}\n\nPage content:\n{pageContent}";

        try
        {
            var raw = await _openAiService.GenerateContentAsync(userPrompt, systemPrompt, requireJson: true);
            return JsonSerializer.Deserialize<CompareContentResponse>(raw, JsonOptions) ?? new CompareContentResponse();
        }
        catch (Exception)
        {
            return new CompareContentResponse
            {
                Without = "The AI provides a generic recommendation relying on third-party sources, without citing your content.",
                With = $"The AI directly references {brand}'s own content when forming its answer.",
                Changed = true,
                Verdict = "Indexing this content gives the AI a first-party source to cite."
            };
        }
    }

    public async Task<BattleResponse> BattleAsync(BattleRequest request)
    {
        var brand = string.IsNullOrWhiteSpace(request.Brand) ? "your brand" : request.Brand;

        var systemPrompt =
            "You estimate how an AI search engine's answer would split its attention between two competing brands " +
            "if a question were subtly framed to favor one of them. Return ONLY a JSON object with EXACTLY these keys: " +
            "\"youPct\" (integer 0-100), \"compPct\" (integer 0-100, youPct + compPct should not exceed 100), " +
            "\"note\" (one short sentence explaining the split).";

        var userPrompt = $"Question: {request.Prompt}\nYour brand: {brand}\nCompetitor (question framed to favor them): {request.Competitor}";

        try
        {
            var raw = await _openAiService.GenerateContentAsync(userPrompt, systemPrompt, requireJson: true);
            return JsonSerializer.Deserialize<BattleResponse>(raw, JsonOptions) ?? new BattleResponse();
        }
        catch (Exception)
        {
            return new BattleResponse
            {
                YouPct = 35,
                CompPct = 65,
                Note = $"When framed to favor {request.Competitor}, {brand} is likely relegated to a secondary mention."
            };
        }
    }

    private static int EstimateConsistency(bool mentioned, int sharePct)
    {
        if (!mentioned) return sharePct >= 20 ? 2 : 1;
        if (sharePct >= 60) return 5;
        if (sharePct >= 40) return 4;
        return 3;
    }
}
