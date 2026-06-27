namespace Citationly.Application.Interfaces;

public interface IOpenRouterService
{
    Task<string> GenerateContentAsync(string prompt, string? systemPrompt = null, bool requireJson = false, string model = "openai/gpt-3.5-turbo");
}
