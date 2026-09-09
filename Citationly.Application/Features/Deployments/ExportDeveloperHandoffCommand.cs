using System.Text;
using MediatR;
using Citationly.Application.Interfaces;
using Citationly.Domain.Entities;

namespace Citationly.Application.Features.Deployments;

public class ExportDeveloperHandoffCommand : IRequest<ExportDeveloperHandoffResult>
{
    public Guid OrganizationId { get; set; }
    public Guid RecommendationId { get; set; }
    public string? SiteType { get; set; }
    public string? TargetPath { get; set; }
    public string? DeliveryOption { get; set; }
}

public record ExportDeveloperHandoffResult(bool Success, string Message, string? FileName, string? Markdown);

public class ExportDeveloperHandoffCommandHandler : IRequestHandler<ExportDeveloperHandoffCommand, ExportDeveloperHandoffResult>
{
    private static readonly HashSet<string> SupportedSiteTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "static-html",
        "html",
        "react",
        "vite",
        "nextjs-app-router",
        "nextjs-pages-router",
        "nextjs",
        "other"
    };

    private readonly IWebsiteRepository _websiteRepository;

    public ExportDeveloperHandoffCommandHandler(IWebsiteRepository websiteRepository)
    {
        _websiteRepository = websiteRepository;
    }

    public async Task<ExportDeveloperHandoffResult> Handle(ExportDeveloperHandoffCommand request, CancellationToken cancellationToken)
    {
        var recommendation = await _websiteRepository.GetRecommendationByIdAsync(request.RecommendationId, request.OrganizationId);
        if (recommendation == null)
        {
            return new ExportDeveloperHandoffResult(false, "Recommendation not found.", null, null);
        }

        var siteType = NormalizeSiteType(request.SiteType);
        if (!SupportedSiteTypes.Contains(siteType))
        {
            return new ExportDeveloperHandoffResult(false, $"Site type '{request.SiteType}' is not supported for developer handoff export yet.", null, null);
        }

        var targetPath = string.IsNullOrWhiteSpace(request.TargetPath)
            ? SuggestedTargetPath(siteType)
            : request.TargetPath.Trim();

        var markdown = BuildMarkdown(recommendation, siteType, targetPath, request.DeliveryOption);
        var slug = Slugify(recommendation.Title);
        return new ExportDeveloperHandoffResult(true, "Developer handoff generated.", $"citationly-{slug}-developer-handoff.md", markdown);
    }

    private static string NormalizeSiteType(string? siteType)
    {
        if (string.IsNullOrWhiteSpace(siteType)) return "other";
        return siteType.Trim().ToLowerInvariant();
    }

    private static string SuggestedTargetPath(string siteType) => siteType switch
    {
        "static-html" or "html" => "index.html or the affected page HTML file",
        "react" or "vite" => "src/pages/*, src/App.tsx, or the matching route/component file",
        "nextjs" or "nextjs-app-router" => "app/.../page.tsx, app/.../layout.tsx, or app/.../metadata.ts",
        "nextjs-pages-router" => "pages/...tsx, next-seo config, or the matching MDX/content file",
        _ => "the affected page/template/component file"
    };

    private static string BuildMarkdown(Recommendation recommendation, string siteType, string targetPath, string? deliveryOption)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"# Citationly developer handoff: {recommendation.Title}");
        builder.AppendLine();
        builder.AppendLine("## Source recommendation");
        builder.AppendLine();
        builder.AppendLine($"- Recommendation ID: `{recommendation.Id}`");
        builder.AppendLine($"- Priority: {ValueOrUnknown(recommendation.Priority)}");
        builder.AppendLine($"- Action type: {ValueOrUnknown(recommendation.ActionType)}");
        builder.AppendLine($"- Current status: {ValueOrUnknown(recommendation.Status)}");
        builder.AppendLine($"- Evidence summary: {ValueOrUnknown(recommendation.Description)}");
        builder.AppendLine();
        builder.AppendLine("## Target implementation");
        builder.AppendLine();
        builder.AppendLine($"- Site type: {siteType}");
        builder.AppendLine($"- Suggested target path: {targetPath}");
        builder.AppendLine($"- Delivery option: {ValueOrDefault(deliveryOption, "Export patch / developer task")}");
        builder.AppendLine();
        builder.AppendLine("## Recommended change set");
        builder.AppendLine();
        foreach (var item in SuggestedChangeSet(recommendation.ActionType, siteType))
        {
            builder.AppendLine($"- {item}");
        }
        builder.AppendLine();
        builder.AppendLine("## Human review checklist");
        builder.AppendLine();
        builder.AppendLine("- Confirm all claims, statistics, product descriptions, prices, and availability against your source of truth.");
        builder.AppendLine("- Confirm the changed page remains brand-safe, legally acceptable, and useful for human readers.");
        builder.AppendLine("- Run your normal checks before merge/deploy: formatting, lint, tests, build, and preview review.");
        builder.AppendLine("- Publish as a reviewed change; do not auto-publish AI-generated copy without approval.");
        builder.AppendLine("- After deploy, re-scan the affected URL/prompts in Citationly and compare before/after evidence.");
        builder.AppendLine();
        builder.AppendLine("## Suggested acceptance criteria");
        builder.AppendLine();
        builder.AppendLine("- The target page clearly answers the visibility gap described above.");
        builder.AppendLine("- The page exposes structured metadata/schema where appropriate.");
        builder.AppendLine("- The change is reversible through your Git/CMS history.");
        builder.AppendLine("- Citationly verification can find the deployed change on the live page.");
        return builder.ToString();
    }

    private static IEnumerable<string> SuggestedChangeSet(string? actionType, string siteType)
    {
        var normalizedAction = actionType?.Trim().ToLowerInvariant() ?? string.Empty;

        if (normalizedAction.Contains("schema") || normalizedAction.Contains("json"))
        {
            yield return siteType.Contains("nextjs")
                ? "Add or update a JSON-LD script in the matching page/layout component or metadata helper."
                : "Add or update JSON-LD in the page head/body according to the schema type required.";
            yield return "Validate the schema with a structured-data validator before deployment.";
            yield break;
        }

        if (normalizedAction.Contains("faq") || normalizedAction.Contains("answer"))
        {
            yield return "Add a concise FAQ or answer block that directly addresses the missing AI-answer coverage.";
            yield return "Include source-backed, factual copy and link to supporting pages where useful.";
            yield return siteType.Contains("nextjs") || siteType is "react" or "vite"
                ? "Use an existing reusable FAQ/accordion/content component if the site already has one."
                : "Keep the section semantic with headings, paragraphs, and accessible markup.";
            yield break;
        }

        if (normalizedAction.Contains("meta") || normalizedAction.Contains("title"))
        {
            yield return siteType.Contains("nextjs")
                ? "Update the page metadata export or SEO component with a specific title and description."
                : "Update the page title and meta description with specific, human-readable copy.";
            yield return "Ensure the metadata accurately reflects the visible page content.";
            yield break;
        }

        yield return "Update the affected page copy so it clearly explains the entity, offering, evidence, and next action.";
        yield return "Add internal links to supporting pages that reinforce the answer for users and AI systems.";
        yield return "Add structured data only when it truthfully represents visible page content.";
    }

    private static string ValueOrUnknown(string? value) => ValueOrDefault(value, "Unknown");

    private static string ValueOrDefault(string? value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }

    private static string Slugify(string value)
    {
        var characters = value
            .Trim()
            .ToLowerInvariant()
            .Select(ch => char.IsLetterOrDigit(ch) ? ch : '-')
            .ToArray();

        var collapsed = string.Join('-', new string(characters).Split('-', StringSplitOptions.RemoveEmptyEntries));
        return string.IsNullOrWhiteSpace(collapsed) ? "recommendation" : collapsed[..Math.Min(collapsed.Length, 64)];
    }
}
