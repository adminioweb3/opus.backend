using MediatR;
using Citationly.Domain.Entities;
using Citationly.Application.Interfaces;

namespace Citationly.Application.Features.Report;

public class GetFullReportQuery : IRequest<GetFullReportResult>
{
    public Guid OrganizationId { get; set; }
}

public class GetFullReportResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public FullReportData? Data { get; set; }
}

public class FullReportData
{
    public WebsiteProfile? WebsiteProfile { get; set; }
    public ExecutiveSummaryData? ExecutiveSummary { get; set; }
    public IEnumerable<Competitor>? Competitors { get; set; }
    public IEnumerable<AiSearchPrompt>? Prompts { get; set; }
    public VisibilitySummary? VisibilitySummary { get; set; }
    public IEnumerable<PlatformVisibility>? PlatformVisibilities { get; set; }
    public CitationSummary? CitationSummary { get; set; }
    public IEnumerable<CitationSource>? CitationSources { get; set; }
    public PersonaAnalysisSummary? PersonaSummary { get; set; }
    public IEnumerable<PersonaScore>? PersonaScores { get; set; }
    public RegionAnalysisSummary? RegionSummary { get; set; }
    public IEnumerable<RegionScore>? RegionScores { get; set; }
    public GeoRecommendationSummary? RecommendationSummary { get; set; }
    public IEnumerable<GeoRecommendation>? Recommendations { get; set; }
}

public class GetFullReportQueryHandler : IRequestHandler<GetFullReportQuery, GetFullReportResult>
{
    private readonly IWebsiteRepository _websiteRepository;

    public GetFullReportQueryHandler(IWebsiteRepository websiteRepository)
    {
        _websiteRepository = websiteRepository;
    }

    public async Task<GetFullReportResult> Handle(GetFullReportQuery request, CancellationToken cancellationToken)
    {
        try
        {
            var data = new FullReportData
            {
                WebsiteProfile = await _websiteRepository.GetLatestWebsiteProfileAsync(request.OrganizationId),
                ExecutiveSummary = await _websiteRepository.GetExecutiveSummaryAsync(request.OrganizationId),
                Competitors = await _websiteRepository.GetCompetitorsAsync(request.OrganizationId),
                Prompts = await _websiteRepository.GetAiSearchPromptsAsync(request.OrganizationId),
                VisibilitySummary = await _websiteRepository.GetVisibilitySummaryAsync(request.OrganizationId),
                PlatformVisibilities = await _websiteRepository.GetPlatformVisibilitiesAsync(request.OrganizationId),
                CitationSummary = await _websiteRepository.GetCitationSummaryAsync(request.OrganizationId),
                CitationSources = await _websiteRepository.GetCitationSourcesAsync(request.OrganizationId),
                PersonaSummary = await _websiteRepository.GetPersonaAnalysisSummaryAsync(request.OrganizationId),
                PersonaScores = await _websiteRepository.GetPersonaScoresAsync(request.OrganizationId),
                RegionSummary = await _websiteRepository.GetRegionAnalysisSummaryAsync(request.OrganizationId),
                RegionScores = await _websiteRepository.GetRegionScoresAsync(request.OrganizationId),
                RecommendationSummary = await _websiteRepository.GetGeoRecommendationSummaryAsync(request.OrganizationId),
                Recommendations = await _websiteRepository.GetGeoRecommendationsAsync(request.OrganizationId)
            };

            return new GetFullReportResult
            {
                Success = true,
                Data = data
            };
        }
        catch (Exception ex)
        {
            return new GetFullReportResult { Success = false, Error = ex.Message };
        }
    }
}
