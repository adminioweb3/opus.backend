namespace Citationly.Application.Features.Assistant.Agents;

public interface IAgent
{
    string Name { get; }
    string Description { get; }
    string[] SupportedIntents { get; }
    
    Task<string> ExecuteAsync(Guid organizationId, string userMessage, string contextJson);
}
