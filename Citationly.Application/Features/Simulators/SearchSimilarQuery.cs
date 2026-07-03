using System.Numerics.Tensors;
using MediatR;
using Citationly.Application.Interfaces;
using Citationly.Domain.Entities;

namespace Citationly.Application.Features.Simulators;

public class SearchSimilarQuery : IRequest<IEnumerable<SearchResult>>
{
    public Guid OrganizationId { get; set; }
    public string QueryText { get; set; } = string.Empty;
    public int TopK { get; set; } = 5;
}

public class SearchResult
{
    public Embedding Embedding { get; set; } = null!;
    public double SimilarityScore { get; set; }
}

public class SearchSimilarQueryHandler : IRequestHandler<SearchSimilarQuery, IEnumerable<SearchResult>>
{
    private readonly IEmbeddingRepository _repository;
    private readonly IAiAnalysisService _aiService;

    public SearchSimilarQueryHandler(IEmbeddingRepository repository, IAiAnalysisService aiService)
    {
        _repository = repository;
        _aiService = aiService;
    }

    public async Task<IEnumerable<SearchResult>> Handle(SearchSimilarQuery request, CancellationToken cancellationToken)
    {
        // 1. Convert the query text into a vector using the AI service
        var queryVector = await _aiService.GenerateEmbeddingAsync(request.QueryText);

        // 2. Fetch all embeddings for this organization
        var allEmbeddings = await _repository.GetEmbeddingsByOrgAsync(request.OrganizationId);

        var results = new List<SearchResult>();

        // 3. Compute cosine similarity in-memory using hardware-accelerated TensorPrimitives
        foreach (var embedding in allEmbeddings)
        {
            // Compute cosine similarity between the query vector and stored vector
            double similarity = TensorPrimitives.CosineSimilarity<double>(queryVector, embedding.Vector);

            results.Add(new SearchResult
            {
                Embedding = embedding,
                SimilarityScore = similarity
            });
        }

        // 4. Return the Top K results sorted by highest similarity
        return results
            .OrderByDescending(x => x.SimilarityScore)
            .Take(request.TopK);
    }
}
