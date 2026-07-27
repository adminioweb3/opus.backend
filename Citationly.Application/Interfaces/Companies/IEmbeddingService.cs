namespace Citationly.Application.Interfaces.Companies;

public interface IEmbeddingService
{
    /// <summary>
    /// Returns a real embedding vector for the given text, or null if the call fails / no key
    /// is configured — callers must treat null as "not yet embedded," never fabricate a vector.
    /// </summary>
    Task<double[]?> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default);

    string ModelName { get; }
}
