using Opus.Domain.Entities;

namespace Opus.Application.Interfaces;

public interface IEmbeddingRepository
{
    Task<Guid> InsertEmbeddingAsync(Embedding embedding);
    Task<IEnumerable<Embedding>> GetEmbeddingsByOrgAsync(Guid organizationId);
}
