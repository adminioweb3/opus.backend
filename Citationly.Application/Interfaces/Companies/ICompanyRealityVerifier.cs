namespace Citationly.Application.Interfaces.Companies;

public interface ICompanyRealityVerifier
{
    Task<CompanyVerificationResult> VerifyAsync(
        string companyName,
        string website,
        CancellationToken cancellationToken);
}

public sealed record CompanyVerificationResult(
    bool IsVerified,
    string? NormalizedDomain,
    string? Reason);
