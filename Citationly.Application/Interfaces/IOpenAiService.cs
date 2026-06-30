namespace Citationly.Application.Interfaces;

public interface IOpenAiService
{
    Task<string> GenerateContentAsync(string prompt, string? systemPrompt = null, bool requireJson = false, string model = "gpt-4o-mini");
}
