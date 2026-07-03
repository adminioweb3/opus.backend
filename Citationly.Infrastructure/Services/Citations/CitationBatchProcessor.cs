using Hangfire;
using Citationly.Application.Interfaces;
using Citationly.Application.Interfaces.Citations;
using Microsoft.Extensions.Logging;

namespace Citationly.Infrastructure.Services.Citations;

public class CitationBatchProcessor : ICitationBatchProcessor
{
    private readonly IWebsiteRepository _websiteRepository;
    private readonly ICitationEnrichmentService _enrichmentService;
    private readonly ICitationAnalyticsService _analyticsService;
    private readonly ILogger<CitationBatchProcessor> _logger;

    public CitationBatchProcessor(
        IWebsiteRepository websiteRepository,
        ICitationEnrichmentService enrichmentService,
        ICitationAnalyticsService analyticsService,
        ILogger<CitationBatchProcessor> logger)
    {
        _websiteRepository = websiteRepository;
        _enrichmentService = enrichmentService;
        _analyticsService = analyticsService;
        _logger = logger;
    }

    public Task QueueCitationEnrichmentAsync(Guid organizationId)
    {
        BackgroundJob.Enqueue(() => ProcessEnrichmentBatchAsync(organizationId));
        return Task.CompletedTask;
    }

    [AutomaticRetry(Attempts = 3)]
    public async Task ProcessEnrichmentBatchAsync(Guid organizationId)
    {
        _logger.LogInformation("Starting citation enrichment batch for OrganizationId: {OrgId}", organizationId);

        // Process in batches of 10
        var batchToProcess = await _websiteRepository.GetCitationsForEnrichmentAsync(organizationId, 10);
        var citations = batchToProcess.ToList();

        if (!citations.Any())
        {
            _logger.LogInformation("No unenriched citations found for OrganizationId: {OrgId}", organizationId);
            return;
        }

        var profile = await _websiteRepository.GetLatestWebsiteProfileAsync(organizationId);
        if (profile == null)
        {
            _logger.LogError("Website profile not found for OrganizationId: {OrgId}. Cannot enrich citations.", organizationId);
            return;
        }

        try
        {
            var enrichedCitations = await _enrichmentService.EnrichCitationsAsync(organizationId, profile.RawProfileJson, citations);
            
            await _websiteRepository.UpdateCitationSourcesAsync(enrichedCitations);
            
            // Trigger deterministic analytics calculation after every batch
            await _analyticsService.ComputeAnalyticsAsync(organizationId);

            // Check if there are more to process
            var remaining = await _websiteRepository.GetCitationsForEnrichmentAsync(organizationId, 1);
            if (remaining.Any())
            {
                // Queue the next batch
                BackgroundJob.Enqueue(() => ProcessEnrichmentBatchAsync(organizationId));
            }
            else
            {
                _logger.LogInformation("Finished enriching all citations for OrganizationId: {OrgId}", organizationId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred during citation enrichment batch for OrganizationId: {OrgId}", organizationId);
            throw; // Rethrow for Hangfire to catch and retry
        }
    }
}
