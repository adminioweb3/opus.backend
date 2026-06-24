namespace Opus.Application.Interfaces;

public interface IOpenRouterService
{
    Task<string> GenerateContentAsync(string prompt);
}
