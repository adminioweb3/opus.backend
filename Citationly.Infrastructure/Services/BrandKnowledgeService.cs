using System.Text.RegularExpressions;
using Citationly.Application.Interfaces;
using Citationly.Domain.Entities;

namespace Citationly.Infrastructure.Services;

public class BrandKnowledgeService : IBrandKnowledgeService
{
    private static readonly Regex SentenceSplitter = new(@"(?<=[.!?])\s+", RegexOptions.Compiled);

    private readonly IPromptIntelligenceRepository _promptRepository;
    private readonly IWebsiteRepository _websiteRepository;

    public BrandKnowledgeService(
        IPromptIntelligenceRepository promptRepository,
        IWebsiteRepository websiteRepository)
    {
        _promptRepository = promptRepository;
        _websiteRepository = websiteRepository;
    }

    public async Task<BrandKnowledgeResult> RefreshAsync(Guid organizationId, int lookbackDays = 30, CancellationToken ct = default)
    {
        var since = DateTime.UtcNow.AddDays(-Math.Clamp(lookbackDays, 1, 365));
        var profile = await _websiteRepository.GetLatestWebsiteProfileAsync(organizationId);
        var brandName = profile?.BusinessName;

        if (string.IsNullOrWhiteSpace(brandName))
        {
            return await GetAsync(organizationId, lookbackDays, ct);
        }

        var sourceRows = await _promptRepository.GetBrandKnowledgeSourceRowsAsync(organizationId, since);
        var claims = ExtractClaims(organizationId, brandName, sourceRows).ToList();
        await _promptRepository.UpsertBrandClaimsAsync(claims);

        var persistedClaims = (await _promptRepository.GetBrandClaimsAsync(organizationId, since)).ToList();
        var factChecks = persistedClaims.Select(claim => CheckClaim(organizationId, claim, profile)).ToList();
        await _promptRepository.UpsertBrandFactChecksAsync(factChecks);

        return await GetAsync(organizationId, lookbackDays, ct);
    }

    public async Task<BrandKnowledgeResult> GetAsync(Guid organizationId, int lookbackDays = 30, CancellationToken ct = default)
    {
        var since = DateTime.UtcNow.AddDays(-Math.Clamp(lookbackDays, 1, 365));
        var claims = (await _promptRepository.GetBrandClaimsAsync(organizationId, since)).ToList();
        var factChecks = (await _promptRepository.GetBrandFactChecksAsync(organizationId, since)).ToList();
        return new BrandKnowledgeResult(
            claims.Count > 0,
            claims,
            factChecks,
            factChecks.Count(f => f.VerificationStatus == "Incorrect"),
            factChecks.Count(f => f.VerificationStatus == "Unverified"));
    }

    private static IEnumerable<BrandClaim> ExtractClaims(Guid organizationId, string brandName, IEnumerable<BrandKnowledgeSourceRow> rows)
    {
        foreach (var row in rows)
        {
            if (string.IsNullOrWhiteSpace(row.ResponseText) ||
                !row.ResponseText.Contains(brandName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (var sentence in SentenceSplitter.Split(row.ResponseText).Select(s => s.Trim()).Where(s => s.Length > 20))
            {
                if (!sentence.Contains(brandName, StringComparison.OrdinalIgnoreCase)) continue;
                var claimType = Classify(sentence);
                if (claimType == null) continue;

                yield return new BrandClaim
                {
                    OrganizationId = organizationId,
                    PromptAnalysisId = row.PromptAnalysisId,
                    PromptResponseId = row.PromptResponseId,
                    PromptQuestionId = row.PromptQuestionId,
                    Platform = row.Platform,
                    ClaimType = claimType,
                    ClaimText = sentence.Length > 500 ? sentence[..500] : sentence,
                    EvidenceQuote = sentence.Length > 300 ? sentence[..300] : sentence,
                    ObservedAt = row.RunAt
                };
            }
        }
    }

    private static string? Classify(string sentence)
    {
        var s = sentence.ToLowerInvariant();
        if (s.Contains("price") || s.Contains("pricing") || s.Contains("cost") || s.Contains("$")) return "Pricing";
        if (s.Contains("feature") || s.Contains("offers") || s.Contains("includes") || s.Contains("supports")) return "Feature";
        if (s.Contains("located") || s.Contains("based in") || s.Contains("headquartered")) return "Location";
        if (s.Contains("founder") || s.Contains("founded") || s.Contains("ceo")) return "Founder";
        if (s.Contains("can ") || s.Contains("helps") || s.Contains("specializes")) return "Capability";
        return null;
    }

    private static BrandFactCheck CheckClaim(Guid organizationId, BrandClaim claim, WebsiteProfile? profile)
    {
        var verifiedCorpus = $"{profile?.BusinessName} {profile?.WebsiteUrl} {profile?.RawProfileJson}".ToLowerInvariant();
        var normalizedClaim = claim.ClaimText.ToLowerInvariant();
        var status = "Unverified";
        var explanation = "The claim was observed in a real AI response, but no matching verified fact was found in the latest website profile.";

        if (!string.IsNullOrWhiteSpace(profile?.BusinessName) &&
            normalizedClaim.Contains(profile.BusinessName.ToLowerInvariant()) &&
            TokenOverlap(normalizedClaim, verifiedCorpus) >= 0.35)
        {
            status = "Verified";
            explanation = "The claim overlaps with verified onboarding/website-profile facts.";
        }
        else if (!string.IsNullOrWhiteSpace(profile?.BusinessName) &&
                 !normalizedClaim.Contains(profile.BusinessName.ToLowerInvariant()))
        {
            status = "Incorrect";
            explanation = "The extracted claim no longer contains the verified brand entity after normalization.";
        }

        return new BrandFactCheck
        {
            OrganizationId = organizationId,
            BrandClaimId = claim.Id,
            VerificationStatus = status,
            VerifiedFact = profile?.RawProfileJson ?? string.Empty,
            Explanation = explanation,
            CheckedAt = DateTime.UtcNow
        };
    }

    private static double TokenOverlap(string claim, string corpus)
    {
        var claimTokens = Tokenize(claim).ToHashSet();
        if (claimTokens.Count == 0) return 0;
        var corpusTokens = Tokenize(corpus).ToHashSet();
        return claimTokens.Count(corpusTokens.Contains) / (double)claimTokens.Count;
    }

    private static IEnumerable<string> Tokenize(string text)
    {
        return Regex.Matches(text.ToLowerInvariant(), "[a-z0-9]{4,}")
            .Select(m => m.Value)
            .Where(t => t is not ("that" or "with" or "from" or "this" or "they" or "their" or "about"));
    }
}
