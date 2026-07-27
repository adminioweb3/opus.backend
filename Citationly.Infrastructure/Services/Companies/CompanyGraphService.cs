using Citationly.Application.Interfaces;
using Citationly.Application.Interfaces.Companies;
using Citationly.Domain.Entities;
using Citationly.Domain.Utils;

namespace Citationly.Infrastructure.Services.Companies;

public class CompanyGraphService : ICompanyGraphService
{
    private static readonly TimeSpan StaleAfter = TimeSpan.FromDays(30);

    private readonly ICompanyRepository _companyRepository;
    private readonly IEmbeddingService _embeddingService;
    private readonly IWebsiteRepository _websiteRepository;

    public CompanyGraphService(
        ICompanyRepository companyRepository,
        IEmbeddingService embeddingService,
        IWebsiteRepository websiteRepository)
    {
        _companyRepository = companyRepository;
        _embeddingService = embeddingService;
        _websiteRepository = websiteRepository;
    }

    public async Task<bool> IsStaleAsync(string websiteUrl)
    {
        var normalized = DomainNormalizer.Normalize(websiteUrl);
        var existing = await _companyRepository.GetByNormalizedDomainAsync(normalized);
        return existing == null || DateTime.UtcNow - existing.LastAnalyzedAt > StaleAfter;
    }

    public async Task<Company> EnsureCompanyAsync(Guid organizationId, string websiteUrl, string businessName, string rawProfileJson, CancellationToken cancellationToken = default)
    {
        var normalized = DomainNormalizer.Normalize(websiteUrl);
        var existing = await _companyRepository.GetByNormalizedDomainAsync(normalized);

        Company company;
        if (existing == null || DateTime.UtcNow - existing.LastAnalyzedAt > StaleAfter)
        {
            var ctx = CompanyProfileSummarizer.ExtractContext(rawProfileJson);
            company = await _companyRepository.UpsertAsync(new Company
            {
                NormalizedDomain = normalized,
                Website = websiteUrl,
                CompanyName = businessName,
                Industry = ctx.Industry,
                BusinessProfileJson = string.IsNullOrWhiteSpace(rawProfileJson) ? "{}" : rawProfileJson,
                SourceOrganizationId = organizationId
            });

            var embeddingText = CompanyProfileSummarizer.BuildEmbeddingText(businessName, rawProfileJson);
            var vector = await _embeddingService.GenerateEmbeddingAsync(embeddingText, cancellationToken);
            if (vector != null)
            {
                await _companyRepository.UpdateEmbeddingAsync(company.Id, vector, _embeddingService.ModelName);
                company.Embedding = vector;
            }
        }
        else
        {
            company = existing;
        }

        var websiteId = await _websiteRepository.GetOrInsertWebsiteAsync(organizationId, websiteUrl);
        await _websiteRepository.LinkWebsiteToCompanyAsync(websiteId, company.Id);

        return company;
    }
}
