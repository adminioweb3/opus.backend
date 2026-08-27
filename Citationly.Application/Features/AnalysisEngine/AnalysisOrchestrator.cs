using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Citationly.Application.Interfaces;
using Citationly.Domain.Entities;

namespace Citationly.Application.Features.AnalysisEngine
{
    public class AnalysisOrchestrator : IAnalysisOrchestrator
    {
        private readonly IAnalysisRepository _repository;
        private readonly IWebsiteRepository _websiteRepository;
        private readonly IScrapingJobRepository _scrapingRepository;

        public AnalysisOrchestrator(
            IAnalysisRepository repository,
            IWebsiteRepository websiteRepository,
            IScrapingJobRepository scrapingRepository)
        {
            _repository = repository;
            _websiteRepository = websiteRepository;
            _scrapingRepository = scrapingRepository;
        }

        public async IAsyncEnumerable<string> ExecuteAnalysisStreamAsync(
            Guid organizationId,
            Guid? websiteId,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            // Step 1: Load Website Data
            yield return "Loading Website Data...";
            var profile = await _websiteRepository.GetLatestWebsiteProfileAsync(organizationId);
            if (profile == null)
            {
                yield return "Error: No website profile found. Please complete onboarding first.";
                yield break;
            }
            string businessName = string.IsNullOrEmpty(profile.BusinessName) ? profile.WebsiteUrl : profile.BusinessName;

            var websites = await _websiteRepository.GetWebsitesByOrgAsync(organizationId);
            var websiteRecord = websites.FirstOrDefault();
            if (websiteRecord == null)
            {
                yield return "Error: No connected website found. Please connect your website first.";
                yield break;
            }

            var run = new AnalysisRun
            {
                OrganizationId = organizationId,
                WebsiteId = websiteRecord.Id, // Use the Website table's ID to satisfy the FK constraint
                StartedAt = DateTime.UtcNow,
                Status = "Running"
            };

            await _repository.CreateAnalysisRunAsync(run);

            // Step 2: Load Crawl Results
            yield return "Loading Crawl Results...";
            var jobs = await _scrapingRepository.GetAllJobsByOrgAsync(organizationId);
            var latestJob = jobs.OrderByDescending(j => j.CreatedAt).FirstOrDefault();
            string websiteContext = "";
            int pagesAnalyzed = 0;

            if (latestJob != null)
            {
                var pages = await _scrapingRepository.GetPagesByJobIdAsync(latestJob.Id);
                var topPages = pages.Where(p => !string.IsNullOrEmpty(p.Content)).Take(5).ToList();
                pagesAnalyzed = topPages.Count;
                foreach (var page in topPages)
                {
                    websiteContext += $"Page Title: {page.Title}\nContent Snippet: {string.Join(" ", page.Content?.Split(' ').Take(100) ?? Array.Empty<string>())}...\n\n";
                }
            }
            if (string.IsNullOrEmpty(websiteContext)) websiteContext = $"Domain: {profile.WebsiteUrl}\nBusiness Name: {profile.BusinessName}";

            // Step 3: Load Competitors
            yield return "Loading Competitors...";
            var competitorsList = await _websiteRepository.GetCompetitorsAsync(organizationId);
            int competitorsCount = competitorsList.Count();
            string competitorContext = string.Join(", ", competitorsList.Select(c => c.Name));
            if (string.IsNullOrEmpty(competitorContext)) competitorContext = "None explicitly defined.";

            // Step 4: Generating Prompts
            yield return "Generating Prompts...";
            run.PromptsExecuted = 3;

            // Step 5: Legacy stream no longer publishes scores; keep it explicit and skip AI calls.
            yield return "Skipping legacy AI analysis...";
            run.ModelsUsed = "none";

            run.CompletedAt = DateTime.UtcNow;
            run.Status = "Unavailable";
            run.DurationSeconds = (int)(run.CompletedAt.Value - run.StartedAt.Value).TotalSeconds;
            run.PagesAnalyzed = pagesAnalyzed;
            run.CompetitorsCompared = competitorsCount;
            await _repository.UpdateAnalysisRunAsync(run);

            yield return "Unavailable: this legacy analysis stream no longer publishes pseudo dashboard scores. Use the prompt intelligence and scan pipelines for evidence-backed scores.";
        }
    }
}
