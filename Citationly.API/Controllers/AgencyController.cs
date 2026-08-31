using System.Security.Cryptography;
using System.Text;
using Citationly.API.Services;
using Citationly.Application.Features.Report;
using Citationly.Application.Interfaces;
using Citationly.Domain.Entities;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Citationly.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AgencyController : ControllerBase
{
    private readonly IAgencyRepository _agencyRepository;
    private readonly ICurrentOrganizationAccessor _currentOrganization;
    private readonly IMediator _mediator;

    public AgencyController(
        IAgencyRepository agencyRepository,
        ICurrentOrganizationAccessor currentOrganization,
        IMediator mediator)
    {
        _agencyRepository = agencyRepository;
        _currentOrganization = currentOrganization;
        _mediator = mediator;
    }

    [Authorize]
    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var ownerOrgId = await _currentOrganization.GetOrganizationIdAsync(User, HttpContext.RequestAborted);
        if (ownerOrgId == null) return Unauthorized();

        var agency = await _agencyRepository.GetAgencyByOwnerOrgAsync(ownerOrgId.Value);
        if (agency == null)
        {
            return Ok(new { configured = false, agency = (Agency?)null, clients = Array.Empty<AgencyClient>(), whiteLabel = (WhiteLabelSettings?)null });
        }

        return Ok(new
        {
            configured = true,
            agency,
            clients = await _agencyRepository.GetClientsAsync(agency.Id),
            whiteLabel = await _agencyRepository.GetWhiteLabelSettingsAsync(agency.Id)
        });
    }

    [Authorize]
    [HttpPost]
    [RequireOrgRole("Admin")]
    [AuditAction("agency.upsert", "Agency", "Agency")]
    public async Task<IActionResult> CreateOrUpdate([FromBody] AgencyRequest request)
    {
        var ownerOrgId = await _currentOrganization.GetOrganizationIdAsync(User, HttpContext.RequestAborted);
        if (ownerOrgId == null) return Unauthorized();

        var agency = await _agencyRepository.CreateOrGetAgencyAsync(ownerOrgId.Value, request.Name?.Trim() ?? string.Empty);
        return Ok(agency);
    }

    [Authorize]
    [HttpGet("clients")]
    public async Task<IActionResult> GetClients()
    {
        var agency = await GetCallerAgencyAsync();
        if (agency.Result != null) return agency.Result;

        return Ok(await _agencyRepository.GetClientsAsync(agency.Agency!.Id));
    }

    [Authorize]
    [HttpPost("clients")]
    [RequireOrgRole("Admin")]
    [AuditAction("agency.client.add", "Agency", "Organization")]
    public async Task<IActionResult> AddClient([FromBody] AgencyClientRequest request)
    {
        var agency = await GetCallerAgencyAsync();
        if (agency.Result != null) return agency.Result;

        if (request.ClientOrganizationId == Guid.Empty)
        {
            return BadRequest(new { message = "Client organization id is required." });
        }

        var client = await _agencyRepository.AddClientAsync(
            agency.Agency!.Id,
            request.ClientOrganizationId,
            request.ClientName?.Trim() ?? string.Empty,
            string.IsNullOrWhiteSpace(request.Role) ? "Manager" : request.Role.Trim());

        return Ok(client);
    }

    [Authorize]
    [HttpGet("white-label")]
    public async Task<IActionResult> GetWhiteLabel()
    {
        var agency = await GetCallerAgencyAsync();
        if (agency.Result != null) return agency.Result;

        return Ok(await _agencyRepository.GetWhiteLabelSettingsAsync(agency.Agency!.Id));
    }

    [Authorize]
    [HttpPut("white-label")]
    [RequireOrgRole("Manager")]
    [AuditAction("agency.white_label.update", "Agency", "WhiteLabelSettings")]
    public async Task<IActionResult> SaveWhiteLabel([FromBody] WhiteLabelRequest request)
    {
        var agency = await GetCallerAgencyAsync();
        if (agency.Result != null) return agency.Result;

        var primaryColor = string.IsNullOrWhiteSpace(request.PrimaryColor) ? "#4F46E5" : request.PrimaryColor.Trim();
        await _agencyRepository.UpsertWhiteLabelSettingsAsync(new WhiteLabelSettings
        {
            AgencyId = agency.Agency!.Id,
            BrandName = request.BrandName?.Trim() ?? string.Empty,
            LogoUrl = request.LogoUrl?.Trim() ?? string.Empty,
            PrimaryColor = primaryColor
        });

        return Ok(await _agencyRepository.GetWhiteLabelSettingsAsync(agency.Agency.Id));
    }

    [Authorize]
    [HttpPost("report-links")]
    [RequireOrgRole("Manager")]
    [AuditAction("agency.report_link.create", "DataExport", "ReportShareLink")]
    public async Task<IActionResult> CreateReportLink([FromBody] ReportLinkRequest request)
    {
        var ownerOrgId = await _currentOrganization.GetOrganizationIdAsync(User, HttpContext.RequestAborted);
        if (ownerOrgId == null) return Unauthorized();

        var agency = await _agencyRepository.GetAgencyByOwnerOrgAsync(ownerOrgId.Value);
        if (agency == null) return BadRequest(new { message = "Create agency settings before sharing white-label reports." });

        var organizationId = request.OrganizationId == Guid.Empty ? ownerOrgId.Value : request.OrganizationId;
        if (!await CanAgencyAccessOrganizationAsync(agency, ownerOrgId.Value, organizationId))
        {
            return Forbid();
        }

        var token = GenerateToken();
        var expiresAt = DateTime.UtcNow.AddDays(Math.Clamp(request.ExpiresInDays, 1, 90));
        var id = await _agencyRepository.CreateReportShareLinkAsync(new ReportShareLink
        {
            AgencyId = agency.Id,
            OrganizationId = organizationId,
            TokenHash = HashToken(token),
            ReportType = string.IsNullOrWhiteSpace(request.ReportType) ? "Executive" : request.ReportType.Trim(),
            ExpiresAt = expiresAt
        });

        var apiUrl = $"{Request.Scheme}://{Request.Host}/api/Agency/public/reports/{token}";
        var shareUrl = $"{Request.Scheme}://{Request.Host}/share/reports/{token}";
        return Ok(new { id, token, expiresAt, apiUrl, shareUrl });
    }

    [Authorize]
    [HttpDelete("report-links/{id:guid}")]
    [RequireOrgRole("Manager")]
    [AuditAction("agency.report_link.revoke", "DataExport", "ReportShareLink")]
    public async Task<IActionResult> RevokeReportLink(Guid id)
    {
        var agency = await GetCallerAgencyAsync();
        if (agency.Result != null) return agency.Result;

        var revoked = await _agencyRepository.RevokeReportShareLinkAsync(id, agency.Agency!.Id);
        return revoked ? NoContent() : NotFound();
    }

    [AllowAnonymous]
    [HttpGet("public/reports/{token}")]
    public async Task<IActionResult> GetSharedReport(string token)
    {
        if (string.IsNullOrWhiteSpace(token)) return NotFound();

        var link = await _agencyRepository.GetReportShareLinkByTokenHashAsync(HashToken(token.Trim()));
        if (link == null) return NotFound();

        var report = await _mediator.Send(new GetFullReportQuery { OrganizationId = link.OrganizationId }, HttpContext.RequestAborted);
        if (!report.Success) return BadRequest(new { message = report.Error });

        WhiteLabelSettings? whiteLabel = null;
        if (link.AgencyId.HasValue)
        {
            whiteLabel = await _agencyRepository.GetWhiteLabelSettingsAsync(link.AgencyId.Value);
        }

        return Ok(new
        {
            provenance = "token_scoped_white_label_report",
            link.ReportType,
            link.ExpiresAt,
            whiteLabel,
            report = report.Data
        });
    }

    private async Task<(Agency? Agency, IActionResult? Result)> GetCallerAgencyAsync()
    {
        var ownerOrgId = await _currentOrganization.GetOrganizationIdAsync(User, HttpContext.RequestAborted);
        if (ownerOrgId == null) return (null, Unauthorized());

        var agency = await _agencyRepository.GetAgencyByOwnerOrgAsync(ownerOrgId.Value);
        return agency == null
            ? (null, BadRequest(new { message = "Create agency settings first." }))
            : (agency, null);
    }

    private async Task<bool> CanAgencyAccessOrganizationAsync(Agency agency, Guid ownerOrgId, Guid organizationId)
    {
        if (organizationId == ownerOrgId) return true;

        var clients = await _agencyRepository.GetClientsAsync(agency.Id);
        return clients.Any(c => c.ClientOrganizationId == organizationId);
    }

    private static string GenerateToken()
    {
        Span<byte> bytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Base64UrlEncode(bytes);
    }

    private static string HashToken(string token)
    {
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Base64UrlEncode(hashBytes);
    }

    private static string Base64UrlEncode(ReadOnlySpan<byte> bytes)
    {
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }
}

public class AgencyRequest
{
    public string? Name { get; set; }
}

public class AgencyClientRequest
{
    public Guid ClientOrganizationId { get; set; }
    public string? ClientName { get; set; }
    public string? Role { get; set; }
}

public class WhiteLabelRequest
{
    public string? BrandName { get; set; }
    public string? LogoUrl { get; set; }
    public string? PrimaryColor { get; set; }
}

public class ReportLinkRequest
{
    public Guid OrganizationId { get; set; }
    public string? ReportType { get; set; }
    public int ExpiresInDays { get; set; } = 30;
}
