namespace Citationly.Application.Interfaces;

public interface IAiResilienceService
{
    Task<T> ExecuteAsync<T>(string operationName, Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken = default);
}
