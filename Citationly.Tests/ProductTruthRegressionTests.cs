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
}
