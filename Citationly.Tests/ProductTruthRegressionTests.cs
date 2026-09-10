using Xunit;

namespace Citationly.Tests;

public partial class ProductTruthRegressionTests
{
    [Fact]
    public void CustomerFacingDashboardRoutes_DoNotImportMockData()
    {
        var repoRoot = FindRepoRoot();
        var dashboardRoot = Path.Combine(repoRoot, "frontend", "src", "app", "(dashboard)", "dashboard");
        var files = Directory.EnumerateFiles(dashboardRoot, "*.tsx", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}admin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var offenders = files
            .Where(path => File.ReadAllText(path).Contains("@/lib/mock-data", StringComparison.Ordinal)
                           || File.ReadAllText(path).Contains("../mock-data", StringComparison.Ordinal)
                           || File.ReadAllText(path).Contains("../../mock-data", StringComparison.Ordinal))
            .Select(path => Path.GetRelativePath(repoRoot, path))
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void ScoreRenderingSurfaces_DoNotGenerateRandomCustomerScores()
    {
        var repoRoot = FindRepoRoot();
        var scoreSurfaceFiles = new[]
        {
            Path.Combine(repoRoot, "frontend", "src", "app", "(dashboard)", "dashboard", "geo", "page.tsx"),
            Path.Combine(repoRoot, "frontend", "src", "app", "(dashboard)", "dashboard", "geo-dashboard", "page.tsx"),
            Path.Combine(repoRoot, "frontend", "src", "app", "(dashboard)", "dashboard", "overview", "page.tsx"),
            Path.Combine(repoRoot, "frontend", "src", "components", "report", "AIVisibilityOverview.tsx"),
            Path.Combine(repoRoot, "frontend", "src", "components", "report", "ExecutiveKPIs.tsx"),
            Path.Combine(repoRoot, "frontend", "src", "components", "report", "FinalScorecard.tsx"),
            Path.Combine(repoRoot, "frontend", "src", "components", "report", "ReportCover.tsx")
        };

        var offenders = scoreSurfaceFiles
            .Where(File.Exists)
            .Where(path => File.ReadAllText(path).Contains("Math.random", StringComparison.Ordinal))
            .Select(path => Path.GetRelativePath(repoRoot, path))
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void DashboardNavigationTargetsExistingRoutes()
    {
        var repoRoot = FindRepoRoot();
        var appRoot = Path.Combine(repoRoot, "frontend", "src", "app");
        var shellFiles = new[]
        {
            Path.Combine(repoRoot, "frontend", "src", "components", "layouts", "DashboardSidebar.tsx"),
            Path.Combine(repoRoot, "frontend", "src", "components", "features", "CommandPalette.tsx"),
            Path.Combine(repoRoot, "frontend", "src", "app", "(dashboard)", "dashboard", "agents", "page.tsx"),
            Path.Combine(repoRoot, "frontend", "src", "app", "(dashboard)", "dashboard", "projects", "page.tsx"),
            Path.Combine(repoRoot, "frontend", "src", "app", "(dashboard)", "dashboard", "monitoring", "page.tsx")
        };

        var routeLiterals = shellFiles
            .Where(File.Exists)
            .SelectMany(path => DashboardRouteRegex().Matches(File.ReadAllText(path)).Select(match => match.Value))
            .Select(value => value.Split('?', 2)[0].TrimEnd('/'))
            .Distinct()
            .Where(route => route != "/dashboard")
            .ToList();

        var missingRoutes = routeLiterals
            .Where(route => !DashboardRouteExists(appRoot, route))
            .ToList();

        Assert.Empty(missingRoutes);
    }

    [Fact]
    public void CustomerFacingControllers_DoNotAcceptClientSuppliedOrganizationIds()
    {
        var repoRoot = FindRepoRoot();
        var controllersRoot = Path.Combine(repoRoot, "backend", "Citationly.API", "Controllers");
        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "AdminController.cs"
        };

        var offenders = Directory.EnumerateFiles(controllersRoot, "*Controller.cs", SearchOption.TopDirectoryOnly)
            .Where(path => !allowed.Contains(Path.GetFileName(path)))
            .Where(path =>
            {
                var text = File.ReadAllText(path);
                return ClientSuppliedOrganizationIdRegex().IsMatch(text);
            })
            .Select(path => Path.GetRelativePath(repoRoot, path))
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void PublicDocs_DoNotOverPromiseUnavailableIntegrationsOrEngineCoverage()
    {
        var repoRoot = FindRepoRoot();
        var publicRoot = Path.Combine(repoRoot, "frontend", "src", "app", "(public)");
        var files = new[]
        {
            Path.Combine(publicRoot, "integrations", "content.tsx"),
            Path.Combine(publicRoot, "integrations", "page.tsx"),
            Path.Combine(publicRoot, "resources", "content.tsx"),
            Path.Combine(publicRoot, "docs", "content.tsx"),
            Path.Combine(publicRoot, "pricing", "content.tsx"),
            Path.Combine(publicRoot, "features", "content.tsx"),
            Path.Combine(publicRoot, "features", "ai-visibility-dashboard", "content.tsx"),
            Path.Combine(publicRoot, "features", "brand-monitoring", "content.tsx"),
            Path.Combine(publicRoot, "features", "citation-tracking", "content.tsx"),
            Path.Combine(repoRoot, "frontend", "src", "components", "features", "landing", "Pricing.tsx"),
            Path.Combine(repoRoot, "frontend", "src", "components", "features", "landing", "ProductShowcase.tsx"),
            Path.Combine(repoRoot, "frontend", "src", "components", "layouts", "navbar", "navData.ts"),
            Path.Combine(repoRoot, "frontend", "src", "app", "(dashboard)", "dashboard", "visibility-radar", "page.tsx")
        };

        var forbiddenClaims = new[]
        {
            "connects to Google Analytics, Google Search Console, Slack, Zapier, and CRM platforms today",
            "Connect Citationly to Google Analytics, Search Console, Slack, Zapier, and your CRM",
            "Visibility Radar runs across 6 engines",
            "query six major engines",
            "continuously monitors six major AI engines",
            "all six engines",
            "checks six AI platforms",
            "3 AI platforms",
            "All 9 AI platforms",
            "All 9 platforms",
            "every AI platform",
            "real-time visibility across ChatGPT",
            "real-time score",
            "Real-time monitoring",
            "starts working immediately",
            "Zapier alone opens the door"
        };

        var offenders = files
            .Where(File.Exists)
            .SelectMany(path => forbiddenClaims
                .Where(claim => File.ReadAllText(path).Contains(claim, StringComparison.OrdinalIgnoreCase))
                .Select(claim => $"{Path.GetRelativePath(repoRoot, path)} contains over-promising claim: {claim}"))
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void SensitiveCustomerControllers_KeepRoleAndAuditGuards()
    {
        var repoRoot = FindRepoRoot();
        var required = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["AlertsController.cs"] = new[] { "[RequireOrgRole(\"Manager\")", "[AuditAction(\"alerts.threshold.update\"" },
            ["ApiKeysController.cs"] = new[] { "[RequireOrgRole(\"Admin\")", "[AuditAction(\"api_key.create\"", "[AuditAction(\"api_key.revoke\"" },
            ["BillingController.cs"] = new[] { "[RequireOrgRole(\"Admin\")", "[AuditAction(\"billing.subscription_session.create\"", "[AuditAction(\"billing.subscription.cancel\"" },
            ["TeamController.cs"] = new[] { "[RequireOrgRole(\"Admin\")", "[AuditAction(\"team.member.role_update\"", "[AuditAction(\"team.member.remove\"", "[AuditAction(\"team.invite.create\"", "[AuditAction(\"team.invite.revoke\"" },
            ["DataLifecycleController.cs"] = new[] { "[RequireOrgRole(\"Admin\")", "[RequireOrgRole(\"Owner\")", "[AuditAction(\"data_lifecycle.deletion_request.create\"" },
            ["EnterpriseSsoController.cs"] = new[] { "[RequireOrgRole(\"Admin\")", "[AuditAction(\"enterprise.sso.upsert\"", "[AuditAction(\"enterprise.scim_token.rotate\"" },
            ["AgencyController.cs"] = new[] { "[RequireOrgRole(\"Admin\")", "[RequireOrgRole(\"Manager\")", "[AuditAction(\"agency.report_link.create\"", "[AuditAction(\"agency.report_link.revoke\"" },
        };

        var missing = new List<string>();
        foreach (var (fileName, markers) in required)
        {
            var path = Path.Combine(repoRoot, "backend", "Citationly.API", "Controllers", fileName);
            var text = File.ReadAllText(path);
            missing.AddRange(markers
                .Where(marker => !text.Contains(marker, StringComparison.Ordinal))
                .Select(marker => $"{Path.GetRelativePath(repoRoot, path)} missing {marker}"));
        }

        Assert.Empty(missing);
    }

    private static bool DashboardRouteExists(string appRoot, string route)
    {
        var segments = route.Trim('/').Split('/');
        if (segments.Length < 2 || segments[0] != "dashboard") return true;

        var current = Path.Combine(appRoot, "(dashboard)");
        foreach (var segment in segments)
        {
            current = Path.Combine(current, segment);
        }

        return File.Exists(Path.Combine(current, "page.tsx"));
    }

    private static string FindRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current != null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, "frontend"))
                && Directory.Exists(Path.Combine(current.FullName, "backend")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root from test output directory.");
    }

    [System.Text.RegularExpressions.GeneratedRegex("/dashboard[/A-Za-z0-9?=\\-]*")]
    private static partial System.Text.RegularExpressions.Regex DashboardRouteRegex();

    [System.Text.RegularExpressions.GeneratedRegex("public\\s+(async\\s+)?(Task<[^>]+>|Task|IActionResult)\\s+\\w+\\s*\\([^)]*(Guid\\??|string)\\s+organizationId\\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase)]
    private static partial System.Text.RegularExpressions.Regex ClientSuppliedOrganizationIdRegex();
}
