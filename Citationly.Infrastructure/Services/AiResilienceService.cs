using System.Collections.Concurrent;
using Citationly.Application.Interfaces;

namespace Citationly.Infrastructure.Services;

public sealed class AiResilienceService : IAiResilienceService
{
    private static readonly ConcurrentDictionary<string, CircuitState> Circuits = new();

    public async Task<T> ExecuteAsync<T>(string operationName, Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken = default)
    {
        if (IsOpen(operationName, out var openUntil))
        {
            throw new InvalidOperationException($"AI service for {operationName} is temporarily unavailable. Try again after {openUntil:O}.");
        }

        var attempt = 0;
        var lastError = default(Exception);

        while (attempt < 3)
        {
            attempt++;
            try
            {
                var result = await action(cancellationToken);
                Reset(operationName);
                return result;
            }
            catch (Exception ex) when (IsTransient(ex))
            {
                lastError = ex;
                RegisterFailure(operationName);

                if (attempt >= 3)
                    break;

                await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt - 1)), cancellationToken);
            }
        }

        throw lastError ?? new InvalidOperationException($"AI call for {operationName} failed.");
    }

    private static bool IsTransient(Exception ex) =>
        ex is HttpRequestException ||
        ex is TaskCanceledException ||
        ex is TimeoutException;

    private static bool IsOpen(string operationName, out DateTimeOffset openUntil)
    {
        if (Circuits.TryGetValue(operationName, out var state) && state.OpenUntilUtc.HasValue && state.OpenUntilUtc.Value > DateTimeOffset.UtcNow)
        {
            openUntil = state.OpenUntilUtc.Value;
            return true;
        }

        openUntil = default;
        return false;
    }

    private static void Reset(string operationName)
    {
        Circuits.AddOrUpdate(operationName, _ => new CircuitState(0, null), (_, __) => new CircuitState(0, null));
    }

    private static void RegisterFailure(string operationName)
    {
        Circuits.AddOrUpdate(operationName,
            _ => new CircuitState(1, null),
            (_, current) =>
            {
                var failures = current.FailureCount + 1;
                if (failures >= 5)
                {
                    return new CircuitState(failures, DateTimeOffset.UtcNow.AddMinutes(2));
                }

                return new CircuitState(failures, null);
            });
    }

    private sealed record CircuitState(int FailureCount, DateTimeOffset? OpenUntilUtc);
}
