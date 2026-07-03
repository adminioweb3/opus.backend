namespace Citationly.Application.Interfaces.Competitors;

public interface ITokenBudgetManager
{
    /// <summary>
    /// Estimates the token count for a given string.
    /// </summary>
    int EstimateTokens(string text);

    /// <summary>
    /// Trims a JSON array string to keep it under a specific token budget.
    /// </summary>
    string TrimToTokenBudget(string jsonArray, int maxTokens);
}
