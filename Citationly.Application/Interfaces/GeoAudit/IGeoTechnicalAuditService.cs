namespace Citationly.Application.Interfaces.GeoAudit;

public interface IGeoTechnicalAuditService
{
    Task<GeoTechnicalAuditResult> AuditAsync(string websiteUrl, CancellationToken cancellationToken = default);
}

public sealed record GeoTechnicalAuditResult(
    string Url,
    int OverallScore,
    int SeoHealthScore,
    int AeoReadinessScore,
    IReadOnlyDictionary<string, int> PillarScores,
    IReadOnlyList<GeoTechnicalCheck> Checks,
    IReadOnlyList<string> EvidenceNotes);

public sealed record GeoTechnicalCheck(
    string Key,
    string Label,
    int Score,
    bool Passed,
    string Evidence);
