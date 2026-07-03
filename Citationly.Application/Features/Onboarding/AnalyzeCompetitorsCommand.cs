using MediatR;
using System.Text.Json;
using Citationly.Application.Interfaces;
using Citationly.Application.Interfaces.Competitors;
using Citationly.Domain.Entities;
using Microsoft.Extensions.DependencyInjection;

namespace Citationly.Application.Features.Onboarding;

public class AnalyzeCompetitorsCommand : IRequest<CompetitorAnalysisResult>
{
    public Guid OrganizationId { get; set; }
}

public class CompetitorAnalysisResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public int TotalCompetitors { get; set; }
    public object? Competitors { get; set; }
    public bool EnrichmentQueued { get; set; }
}

public class CompAnalysisResponse
{
    public CompBusiness? business { get; set; }
    public List<CompCompetitor>? competitors { get; set; }
    public CompSummary? summary { get; set; }
}

public class CompBusiness
{
    public string? name { get; set; }
    public string? website { get; set; }
    public string? industry { get; set; }
}

public class CompSummary
{
    public int totalCompetitors { get; set; }
    public int directCompetitors { get; set; }
    public int indirectCompetitors { get; set; }
    public string? industry { get; set; }
    public string? marketOverview { get; set; }
}

/// <summary>
/// Lightweight discovery DTO – only 8 fields for ultra-fast AI response (~2K tokens).
/// Heavy enrichment data (SEO, Traffic, etc.) is handled asynchronously in Stage 4.
/// </summary>
public class CompCompetitor
{
    public int rank { get; set; }
    public string? companyName { get; set; }
    public string? website { get; set; }
    public string? industry { get; set; }
    public string? competitorType { get; set; }
    public string? description { get; set; }
    public int similarityScore { get; set; }
    public int confidence { get; set; }
}


public class AnalyzeCompetitorsCommandHandler : IRequestHandler<AnalyzeCompetitorsCommand, CompetitorAnalysisResult>
{
    private readonly IWebsiteRepository _websiteRepository;
    private readonly ICompetitorDiscoveryService _discoveryService;
    private readonly ICompetitorCacheService _cacheService;
    private readonly IServiceScopeFactory _scopeFactory;

    public AnalyzeCompetitorsCommandHandler(
        IWebsiteRepository websiteRepository,
        ICompetitorDiscoveryService discoveryService,
        ICompetitorCacheService cacheService,
        IServiceScopeFactory scopeFactory)
    {
        _websiteRepository = websiteRepository;
        _discoveryService = discoveryService;
        _cacheService = cacheService;
        _scopeFactory = scopeFactory;
    }

    public async Task<CompetitorAnalysisResult> Handle(AnalyzeCompetitorsCommand request, CancellationToken cancellationToken)
    {
        // Stage 1: Smart Cache Check
        var (isValid, cachedCompetitors) = await _cacheService.TryGetCachedAsync(request.OrganizationId, cancellationToken);
        if (isValid && cachedCompetitors != null)
        {
            var cachedList = cachedCompetitors.ToList();
            return new CompetitorAnalysisResult
            {
                Success = true,
                TotalCompetitors = cachedList.Count,
                Competitors = cachedList,
                EnrichmentQueued = cachedList.Any(c => c.EnrichmentStatus == "Pending" || c.EnrichmentStatus == "InProgress")
            };
        }

        // Stage 2: Get Business Intelligence Profile
        var profile = await _websiteRepository.GetLatestWebsiteProfileAsync(request.OrganizationId);
        if (profile == null)
        {
            return new CompetitorAnalysisResult { Success = false, Error = "No website profile found. Run analysis step first." };
        }

        // Clear stale competitors if cache was invalid
        await _websiteRepository.DeleteCompetitorsByOrgAsync(request.OrganizationId);

        // Stage 3: Lightweight AI Discovery (~2K tokens, <20s)
        var competitors = await _discoveryService.DiscoverCompetitorsAsync(
            profile.RawProfileJson ?? "",
            profile.WebsiteUrl ?? "",
            profile.BusinessName ?? "",
            request.OrganizationId,
            cancellationToken);

        if (competitors == null || !competitors.Any())
        {
            return new CompetitorAnalysisResult { Success = false, Error = "Failed to discover competitors." };
        }

        // Stage 4: Save discovered competitors immediately
        var dbCompetitors = competitors.Select(c => new Competitor
        {
            Id = Guid.NewGuid(),
            OrganizationId = request.OrganizationId,
            Name = c.companyName ?? "Unknown",
            WebsiteUrl = c.website ?? "",
            Industry = c.industry ?? "",
            Description = c.description ?? "",
            Category = c.competitorType ?? "Direct",
            CompetitorType = c.competitorType ?? "Direct",
            Confidence = c.confidence,
            Rank = c.rank,
            SimilarityScore = c.similarityScore,
            EnrichmentStatus = "Pending",
            CreatedAt = DateTime.UtcNow,
            RawJson = JsonSerializer.Serialize(c)
        }).ToList();

        await _websiteRepository.InsertCompetitorsAsync(dbCompetitors);

        // Stage 5: Queue background enrichment (fire-and-forget, does NOT block API)
        var orgId = request.OrganizationId;
        var profileJson = profile.RawProfileJson ?? "";
        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var enrichmentService = scope.ServiceProvider.GetRequiredService<ICompetitorEnrichmentService>();
                var repo = scope.ServiceProvider.GetRequiredService<IWebsiteRepository>();

                // Enrich top 10 by similarity score
                var topCompetitors = dbCompetitors
                    .OrderByDescending(c => c.SimilarityScore)
                    .Take(10)
                    .ToList();

                Console.WriteLine($"[Enrichment] Starting background enrichment for {topCompetitors.Count} competitors...");
                var sw = System.Diagnostics.Stopwatch.StartNew();

                // Concurrent enrichment with SemaphoreSlim(3)
                using var semaphore = new SemaphoreSlim(3);
                var enrichmentTasks = topCompetitors.Select(async comp =>
                {
                    await semaphore.WaitAsync();
                    try
                    {
                        await enrichmentService.EnrichCompetitorAsync(comp, profileJson, CancellationToken.None);
                        await repo.UpdateCompetitorAsync(comp);
                        Console.WriteLine($"[Enrichment] Completed: {comp.Name} ({sw.ElapsedMilliseconds}ms)");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[Enrichment] Failed: {comp.Name} - {ex.Message}");
                        comp.EnrichmentStatus = "Failed";
                        await repo.UpdateCompetitorAsync(comp);
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                });

                await Task.WhenAll(enrichmentTasks);
                sw.Stop();
                Console.WriteLine($"[Enrichment] All enrichments completed in {sw.ElapsedMilliseconds}ms");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Enrichment] Background enrichment failed: {ex.Message}");
            }
        });

        return new CompetitorAnalysisResult
        {
            Success = true,
            TotalCompetitors = dbCompetitors.Count,
            Competitors = dbCompetitors,
            EnrichmentQueued = true
        };
    }
}
