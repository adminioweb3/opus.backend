using Citationly.Application.Interfaces.Competitors;

namespace Citationly.Infrastructure.Services.Competitors;

public class TokenBudgetManager : ITokenBudgetManager
{
    // A simple heuristic: 1 token is roughly 4 characters in English text
    private const int CharsPerToken = 4;

    public int EstimateTokens(string text)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        return text.Length / CharsPerToken;
    }

    public string TrimToTokenBudget(string jsonArray, int maxTokens)
    {
        if (string.IsNullOrEmpty(jsonArray)) return "[]";

        int estimatedTokens = EstimateTokens(jsonArray);
        if (estimatedTokens <= maxTokens)
        {
            return jsonArray;
        }

        // If it's a JSON array, we aggressively truncate the string to stay under budget
        // while trying to keep it vaguely valid, though the LLM should handle cutoff text gracefully if instructed.
        // A better approach is truncating the characters directly.
        int maxChars = maxTokens * CharsPerToken;
        
        if (jsonArray.Length > maxChars)
        {
            return jsonArray.Substring(0, maxChars) + "...(truncated due to token limits)";
        }

        return jsonArray;
    }
}
