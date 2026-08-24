using System.Security.Claims;
using Citationly.Application.Interfaces;
using Citationly.Domain.Entities;
using Citationly.Application.Features.PromptIntelligence.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Citationly.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class PromptIntelligenceController : ControllerBase
{
    private readonly IPromptIntelligenceRepository _repo;
    private readonly IUserRepository _userRepository;
    private readonly IWebsiteRepository _websiteRepository;
    private readonly IPromptExecutionService _executionService;
    private readonly IQueryFanoutService _fanoutService;
    private readonly ITopicPromptGeneratorService _topicPromptGenerator;
    private readonly IPromptTopicSeedingService _seedingService;
    private readonly IEntitlementService _entitlements;
    private readonly ILogger<PromptIntelligenceController> _logger;

    public PromptIntelligenceController(
        IPromptIntelligenceRepository repo,
        IUserRepository userRepository,
        IWebsiteRepository websiteRepository,
        IPromptExecutionService executionService,
        IQueryFanoutService fanoutService,
        ITopicPromptGeneratorService topicPromptGenerator,
        IPromptTopicSeedingService seedingService,
        IEntitlementService entitlements,
        ILogger<PromptIntelligenceController> logger)
    {
        _repo = repo;
        _userRepository = userRepository;
        _websiteRepository = websiteRepository;
        _executionService = executionService;
        _fanoutService = fanoutService;
        _topicPromptGenerator = topicPromptGenerator;
        _seedingService = seedingService;
        _entitlements = entitlements;
        _logger = logger;
    }

    private async Task<Guid?> GetOrganizationIdAsync()
    {
        var firebaseUid = User.FindFirst("user_id")?.Value ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(firebaseUid)) return null;

        var user = await _userRepository.GetUserByFirebaseUidAsync(firebaseUid);
        return user?.OrganizationId;
    }

    /// <summary>
    /// Walks Analysis -> Question -> Topic to confirm the analysis belongs to the given org.
    /// Returns the topic so callers that already need it (e.g. seeding) don't re-fetch.
    /// </summary>
    private async Task<PromptTopic?> GetOwningTopicForQuestionAsync(Guid questionId, Guid organizationId)
    {
        var question = await _repo.GetQuestionAsync(questionId);
        if (question == null) return null;
        var topic = await _repo.GetTopicAsync(question.PromptTopicId);
        if (topic == null || topic.OrganizationId != organizationId) return null;
        return topic;
    }

    [HttpGet("topics")]
    public async Task<IActionResult> GetTopics()
    {
        var orgId = await GetOrganizationIdAsync();
        if (orgId == null) return Unauthorized();

        var topics = (await _repo.GetTopicsAsync(orgId.Value)).ToList();

        if (topics.Count == 0)
        {
            await _seedingService.EnsureSeededAsync(orgId.Value);
            topics = (await _repo.GetTopicsAsync(orgId.Value)).ToList();
        }

        return Ok(topics);
    }

    [HttpPost("topics")]
    public async Task<IActionResult> CreateTopic([FromBody] PromptTopic topic)
    {
        var orgId = await GetOrganizationIdAsync();
        if (orgId == null) return Unauthorized();

        topic.OrganizationId = orgId.Value;
        topic.Id = await _repo.CreateTopicAsync(topic);
        return Ok(topic);
    }

    [HttpGet("topics/{topicId}/questions")]
    public async Task<IActionResult> GetQuestions(Guid topicId)
    {
        var orgId = await GetOrganizationIdAsync();
        if (orgId == null) return Unauthorized();

        var topic = await _repo.GetTopicAsync(topicId);
        if (topic == null || topic.OrganizationId != orgId.Value) return NotFound();

        var questions = await _repo.GetQuestionsByTopicAsync(topicId);

        // Add latest visibility data to each question
        var results = new List<object>();
        foreach (var q in questions)
        {
            var analysis = await _repo.GetLatestAnalysisAsync(q.Id);
            PromptVisibility? vis = null;
            if (analysis != null && analysis.Status == "Completed")
            {
                vis = await _repo.GetVisibilityAsync(analysis.Id);
            }
            results.Add(new { Question = q, LatestAnalysis = analysis, Visibility = vis });
        }

        return Ok(results);
    }

    [HttpPost("questions")]
    public async Task<IActionResult> CreateQuestion([FromBody] PromptQuestion question)
    {
        var orgId = await GetOrganizationIdAsync();
        if (orgId == null) return Unauthorized();

        var topic = await _repo.GetTopicAsync(question.PromptTopicId);
        if (topic == null || topic.OrganizationId != orgId.Value) return NotFound();

        question.Id = await _repo.CreateQuestionAsync(question);
        return Ok(question);
    }

    [HttpPatch("questions/{questionId}")]
    public async Task<IActionResult> UpdateQuestion(Guid questionId, [FromBody] UpdateQuestionRequest request)
    {
        var orgId = await GetOrganizationIdAsync();
        if (orgId == null) return Unauthorized();

        var topic = await GetOwningTopicForQuestionAsync(questionId, orgId.Value);
        if (topic == null) return NotFound();

        await _repo.UpdateQuestionAsync(questionId, request.PromptText, request.IsActive);
        var updated = await _repo.GetQuestionAsync(questionId);
        return Ok(updated);
    }

    [HttpGet("analyses/{analysisId}")]
    public async Task<IActionResult> GetAnalysisResults(Guid analysisId)
    {
        var orgId = await GetOrganizationIdAsync();
        if (orgId == null) return Unauthorized();

        var analysis = await _repo.GetAnalysisAsync(analysisId);
        if (analysis == null) return NotFound();
        var owningTopic = await GetOwningTopicForQuestionAsync(analysis.PromptQuestionId, orgId.Value);
        if (owningTopic == null) return NotFound();

        var visibility = await _repo.GetVisibilityAsync(analysisId);
        var mentions = await _repo.GetMentionsAsync(analysisId);
        var responses = await _repo.GetResponsesAsync(analysisId);
        var recommendations = await _repo.GetRecommendationsAsync(analysisId);
        var competitors = await _repo.GetCompetitorComparisonsAsync(analysisId);

        return Ok(new
        {
            Visibility = visibility,
            Mentions = mentions,
            Responses = responses,
            Recommendations = recommendations,
            CompetitorComparisons = competitors
        });
    }

    [HttpGet("analyze/stream/{questionId}")]
    public async Task ExecuteAnalysisStream(Guid questionId)
    {
        var orgId = await GetOrganizationIdAsync();
        if (orgId == null)
        {
            Response.StatusCode = 401;
            return;
        }

        var owningTopic = await GetOwningTopicForQuestionAsync(questionId, orgId.Value);
        if (owningTopic == null)
        {
            Response.StatusCode = 404;
            return;
        }

        Response.Headers.Append("Content-Type", "text/event-stream");
        Response.Headers.Append("Cache-Control", "no-cache");
        Response.Headers.Append("Connection", "keep-alive");

        async Task WriteFrameAsync(string payload)
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes($"data: {payload}\n\n");
            await Response.Body.WriteAsync(bytes, 0, bytes.Length);
            await Response.Body.FlushAsync();
        }

        // Once headers are sent (the client already got a 200), an unhandled exception here can't
        // be turned into a proper error response — the connection just aborts mid-stream, which is
        // exactly what "ERR_INCOMPLETE_CHUNKED_ENCODING" on the client is. Catch here so a failed
        // analysis ends the stream cleanly with a real error message instead of a dead connection.
        try
        {
            var stream = _executionService.ExecutePromptAnalysisAsync(orgId.Value, questionId, HttpContext.RequestAborted);
            await foreach (var message in stream)
            {
                await WriteFrameAsync(message);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Answer Atlas analysis failed for question {QuestionId}", questionId);
            var safeMessage = ex.Message.Replace("\"", "'").Replace("\n", " ");
            await WriteFrameAsync($"{{\"error\": \"Analysis failed: {safeMessage}\"}}");
        }

        await WriteFrameAsync("[DONE]");
    }

    /// <summary>
    /// Answer Atlas's Visibility tab. Aggregates real PromptVisibility rows recorded so far —
    /// analysis in v1 is on-demand (via analyze/stream above), not a recurring daily job, so the
    /// history reflects whatever days actually had a run, rather than a synthetic daily cadence.
    /// </summary>
    [HttpGet("visibility-summary")]
    public async Task<IActionResult> GetVisibilitySummary([FromQuery] string range = "30D")
    {
        var orgId = await GetOrganizationIdAsync();
        if (orgId == null) return Unauthorized();

        var days = range switch { "7D" => 7, "90D" => 90, _ => 30 };
        var since = DateTime.UtcNow.AddDays(-days);

        var rows = (await _repo.GetVisibilitySummaryDataAsync(orgId.Value, since)).ToList();

        if (rows.Count == 0)
        {
            return Ok(new
            {
                hasData = false,
                compositeScore = 0,
                compositeDelta = "+0",
                shareOfVoice = 0,
                shareOfVoiceDelta = "+0",
                averagePosition = 0,
                averagePositionDelta = "+0",
                scoreHistory = Array.Empty<object>(),
                topics = Array.Empty<object>(),
                visibilityRank = new { position = (int?)null, positionDelta = 0, rows = Array.Empty<object>() },
                shareOfVoiceRank = new { position = (int?)null, positionDelta = 0, rows = Array.Empty<object>() },
            });
        }

        // Split the range in half by time to compute a directional delta from real data, rather
        // than a synthetic day-over-day number that on-demand (non-daily) runs can't support.
        var midpoint = since.AddDays(days / 2.0);
        var olderHalf = rows.Where(r => r.RunAt < midpoint).ToList();
        var recentHalf = rows.Where(r => r.RunAt >= midpoint).ToList();

        var compositeScore = Avg(rows.Select(r => r.OverallVisibilityScore));
        var shareOfVoice = Avg(rows.Select(r => r.ShareOfVoice));
        var averagePosition = Avg(rows.Select(r => r.AveragePosition));

        var scoreHistory = rows
            .GroupBy(r => r.RunAt.Date)
            .OrderBy(g => g.Key)
            .Select(g => new { date = g.Key.ToString("yyyy-MM-dd"), score = Math.Round(Avg(g.Select(r => r.OverallVisibilityScore)), 1) });

        var totalCitationCount = rows.Sum(r => r.CitationCount);

        var topics = rows
            .GroupBy(r => new { r.TopicId, r.TopicName })
            .Select(g => new
            {
                topicId = g.Key.TopicId,
                topicName = g.Key.TopicName,
                promptCount = g.Select(r => r.QuestionId).Distinct().Count(),
                score = Math.Round(Avg(g.Select(r => r.OverallVisibilityScore)), 1),
                shareOfVoice = Math.Round(Avg(g.Select(r => r.ShareOfVoice)), 1),
                averagePosition = Math.Round(Avg(g.Select(r => r.AveragePosition)), 1),
                citationCount = g.Sum(r => r.CitationCount),
                citationShare = totalCitationCount == 0 ? 0 : Math.Round((double)g.Sum(r => r.CitationCount) / totalCitationCount * 100, 1),
            })
            .OrderByDescending(t => t.score)
            .Select((t, i) => new
            {
                // A rank number (and the frontend's "Leader" badge built on it) implies real
                // competitive standing. Assigning #1 to whichever topic merely happens to sort
                // first among an all-zero tie — which stable-sorts to "whichever was analyzed
                // first" — reads as "you're winning" when there's no brand mention at all. Only
                // rank topics that actually have measured visibility; leave the rest unranked, the
                // same as topics with no completed analysis yet.
                rank = t.score > 0 ? (int?)(i + 1) : null,
                t.topicId,
                t.topicName,
                t.promptCount,
                t.score,
                t.shareOfVoice,
                t.averagePosition,
                t.citationCount,
                t.citationShare,
            })
            .ToList();

        // Rank Citationly's own aggregate against real tracked competitors, using the same
        // CompetitorComparison rows VisibilityCalculatorService already writes per analysis.
        var competitorRows = (await _repo.GetCompetitorComparisonSummaryDataAsync(orgId.Value, since)).ToList();
        var profile = await _websiteRepository.GetLatestWebsiteProfileAsync(orgId.Value);
        var brandName = profile?.BusinessName ?? "Your brand";

        var competitorGroups = competitorRows
            .GroupBy(r => r.CompetitorName)
            .Select(g => new
            {
                Name = g.Key,
                Score = Avg(g.Select(r => r.VisibilityScore)),
                Sov = Avg(g.Select(r => r.ShareOfVoice)),
                OlderScore = Avg(g.Where(r => r.RunAt < midpoint).Select(r => r.VisibilityScore)),
                RecentScore = Avg(g.Where(r => r.RunAt >= midpoint).Select(r => r.VisibilityScore)),
                OlderSov = Avg(g.Where(r => r.RunAt < midpoint).Select(r => r.ShareOfVoice)),
                RecentSov = Avg(g.Where(r => r.RunAt >= midpoint).Select(r => r.ShareOfVoice)),
            })
            .ToList();

        var ownOlderScore = Avg(olderHalf.Select(r => r.OverallVisibilityScore));
        var ownRecentScore = Avg(recentHalf.Select(r => r.OverallVisibilityScore));
        var ownOlderSov = Avg(olderHalf.Select(r => r.ShareOfVoice));
        var ownRecentSov = Avg(recentHalf.Select(r => r.ShareOfVoice));

        var scoreEntities = competitorGroups
            .Select(c => new RankEntity { Name = c.Name, Owned = false, Value = c.Score, OlderValue = c.OlderScore, RecentValue = c.RecentScore })
            .Append(new RankEntity { Name = brandName, Owned = true, Value = compositeScore, OlderValue = ownOlderScore, RecentValue = ownRecentScore })
            .ToList();

        var sovEntities = competitorGroups
            .Select(c => new RankEntity { Name = c.Name, Owned = false, Value = c.Sov, OlderValue = c.OlderSov, RecentValue = c.RecentSov })
            .Append(new RankEntity { Name = brandName, Owned = true, Value = shareOfVoice, OlderValue = ownOlderSov, RecentValue = ownRecentSov })
            .ToList();

        object BuildRankBlock(List<RankEntity> entities)
        {
            var sortedNow = entities.OrderByDescending(e => e.Value).ToList();
            var sortedOlder = entities.OrderByDescending(e => e.OlderValue).ToList();
            var ownIndexNow = sortedNow.FindIndex(e => e.Owned);
            var ownIndexOlder = sortedOlder.FindIndex(e => e.Owned);
            var positionDelta = ownIndexOlder >= 0 && ownIndexNow >= 0 ? ownIndexOlder - ownIndexNow : 0;

            return new
            {
                position = ownIndexNow >= 0 ? ownIndexNow + 1 : (int?)null,
                positionDelta,
                rows = sortedNow.Select((e, i) => new
                {
                    rank = i + 1,
                    name = e.Name,
                    owned = e.Owned,
                    value = Math.Round(e.Value, 1),
                    delta = Delta(e.RecentValue, e.OlderValue),
                }),
            };
        }

        return Ok(new
        {
            hasData = true,
            compositeScore = Math.Round(compositeScore, 1),
            compositeDelta = Delta(ownRecentScore, ownOlderScore),
            shareOfVoice = Math.Round(shareOfVoice, 1),
            shareOfVoiceDelta = Delta(ownRecentSov, ownOlderSov),
            averagePosition = Math.Round(averagePosition, 1),
            averagePositionDelta = Delta(Avg(recentHalf.Select(r => r.AveragePosition)), Avg(olderHalf.Select(r => r.AveragePosition))),
            scoreHistory,
            topics,
            visibilityRank = BuildRankBlock(scoreEntities),
            shareOfVoiceRank = BuildRankBlock(sovEntities),
        });
    }

    private static (int Days, DateTime Since) ResolveRange(string range)
    {
        var days = range switch { "7D" => 7, "90D" => 90, _ => 30 };
        return (days, DateTime.UtcNow.AddDays(-days));
    }

    private static double Avg(IEnumerable<int> values)
    {
        var list = values.ToList();
        return list.Count == 0 ? 0 : list.Average();
    }

    private static string Delta(double recent, double older)
    {
        if (older == 0) return "+0";
        var diff = recent - older;
        return diff >= 0 ? $"+{diff:0.#}" : diff.ToString("0.#");
    }

    /// <summary>
    /// Platforms tab. Needs no new AI and no new table — PromptResponses/PromptMentions already
    /// carry a Platform field per row, so per-platform score/position/share-of-voice is a pure
    /// aggregation over data the analysis pipeline already writes.
    /// </summary>
    [HttpGet("platforms-summary")]
    public async Task<IActionResult> GetPlatformsSummary([FromQuery] string range = "30D")
    {
        var orgId = await GetOrganizationIdAsync();
        if (orgId == null) return Unauthorized();

        var (_, since) = ResolveRange(range);
        var rows = (await _repo.GetPlatformSummaryDataAsync(orgId.Value, since)).ToList();
        var citationRows = (await _repo.GetCitationSummaryDataAsync(orgId.Value, since)).ToList();

        if (rows.Count == 0) return Ok(new { hasData = false, platforms = Array.Empty<object>() });

        var totalCitations = citationRows.Count;
        var citationsByPlatform = citationRows.GroupBy(c => c.Platform).ToDictionary(g => g.Key, g => g.Count());
        var totalResponsesByPlatform = rows.GroupBy(r => r.Platform).ToDictionary(g => g.Key, g => g.Count());

        double ScoreFor(int mentionCount, int totalResponses, IEnumerable<int> positions)
        {
            var mentionFrequency = totalResponses == 0 ? 0 : (double)mentionCount / totalResponses * 100;
            var avgPosition = positions.DefaultIfEmpty(0).Average();
            return Math.Clamp((mentionFrequency * 2) - (avgPosition / 2), 0, 100);
        }

        var platforms = rows
            .GroupBy(r => r.Platform)
            .Select(g =>
            {
                var total = g.Count();
                var mentioned = g.Count(r => r.IsBrandMentioned);
                var avgPosition = g.Where(r => r.BrandPosition.HasValue).Select(r => r.BrandPosition!.Value).DefaultIfEmpty(0).Average();
                var score = ScoreFor(mentioned, total, g.Where(r => r.BrandPosition.HasValue).Select(r => r.BrandPosition!.Value));
                var totalMentions = g.Sum(r => r.TotalMentionsOnPlatform);
                var brandMentions = g.Sum(r => r.BrandMentionsOnPlatform);
                var shareOfVoice = totalMentions == 0 ? 0 : (double)brandMentions / totalMentions * 100;
                var citationCount = citationsByPlatform.GetValueOrDefault(g.Key, 0);
                var citationShare = totalCitations == 0 ? 0 : (double)citationCount / totalCitations * 100;

                return new
                {
                    platform = g.Key,
                    score = Math.Round(score, 1),
                    shareOfVoice = Math.Round(shareOfVoice, 1),
                    averagePosition = Math.Round(avgPosition, 1),
                    citationShare = Math.Round(citationShare, 1),
                };
            })
            .OrderByDescending(p => p.score)
            .ToList();

        // Real competitor x platform matrix — PromptMentions already carries Platform per
        // mention, so this is derived the same way the brand's own per-platform score is,
        // just grouped by competitor name instead.
        var competitorMentionRows = (await _repo.GetCompetitorMentionSummaryDataAsync(orgId.Value, since)).ToList();
        var profile = await _websiteRepository.GetLatestWebsiteProfileAsync(orgId.Value);
        var brandName = profile?.BusinessName ?? "Your brand";
        var platformNames = platforms.Select(p => p.platform).ToList();

        var matrixRows = new List<object>
        {
            new
            {
                name = brandName,
                owned = true,
                values = platformNames.Select(p => platforms.First(pl => pl.platform == p).score).ToList(),
            }
        };

        matrixRows.AddRange(competitorMentionRows
            .GroupBy(r => r.CompetitorName)
            .Select(g => new
            {
                name = g.Key,
                owned = false,
                values = platformNames.Select(p =>
                {
                    var onPlatform = g.Where(r => r.Platform == p).ToList();
                    var total = totalResponsesByPlatform.GetValueOrDefault(p, 0);
                    return Math.Round(ScoreFor(onPlatform.Count, total, onPlatform.Select(r => r.Position)), 1);
                }).ToList(),
            })
            .Cast<object>());

        return Ok(new { hasData = true, platforms, matrix = new { platformNames, rows = matrixRows } });
    }

    /// <summary>
    /// Shared by Regions and Personas tabs — same rollup shape as the Visibility tab's topic
    /// table, grouped by PromptQuestion.Region/.Persona instead of topic.
    /// </summary>
    private async Task<IActionResult> GetGroupedSummaryAsync(Guid organizationId, string range, Func<PromptVisibilitySummaryRow, string?> keySelector, string defaultKey)
    {
        var (_, since) = ResolveRange(range);
        var rows = (await _repo.GetVisibilitySummaryDataAsync(organizationId, since)).ToList();

        if (rows.Count == 0) return Ok(new { hasData = false, groups = Array.Empty<object>() });

        var groups = rows
            .GroupBy(r => string.IsNullOrWhiteSpace(keySelector(r)) ? defaultKey : keySelector(r)!)
            .Select(g => new
            {
                name = g.Key,
                promptCount = g.Select(r => r.QuestionId).Distinct().Count(),
                score = Math.Round(Avg(g.Select(r => r.OverallVisibilityScore)), 1),
                shareOfVoice = Math.Round(Avg(g.Select(r => r.ShareOfVoice)), 1),
                averagePosition = Math.Round(Avg(g.Select(r => r.AveragePosition)), 1),
                citationCount = g.Sum(r => r.CitationCount),
            })
            .OrderByDescending(g => g.score)
            .ToList();

        return Ok(new { hasData = true, groups });
    }

    [HttpGet("regions-summary")]
    public async Task<IActionResult> GetRegionsSummary([FromQuery] string range = "30D")
    {
        var orgId = await GetOrganizationIdAsync();
        if (orgId == null) return Unauthorized();

        if (!await _entitlements.CanUseFeatureAsync(orgId.Value, "regions_summary"))
        {
            var planType = await _entitlements.GetPlanKeyAsync(orgId.Value);
            return StatusCode(403, new { error = "Regional breakdowns are available on the Enterprise plan.", planType });
        }

        return await GetGroupedSummaryAsync(orgId.Value, range, r => r.Region, "Global");
    }

    [HttpGet("personas-summary")]
    public async Task<IActionResult> GetPersonasSummary([FromQuery] string range = "30D")
    {
        var orgId = await GetOrganizationIdAsync();
        if (orgId == null) return Unauthorized();

        if (!await _entitlements.CanUseFeatureAsync(orgId.Value, "personas_summary"))
        {
            var planType = await _entitlements.GetPlanKeyAsync(orgId.Value);
            return StatusCode(403, new { error = "Persona breakdowns are available on the Enterprise plan.", planType });
        }

        return await GetGroupedSummaryAsync(orgId.Value, range, r => r.Persona, "Unspecified");
    }

    [HttpGet("sentiment-summary")]
    public async Task<IActionResult> GetSentimentSummary([FromQuery] string range = "30D")
    {
        var orgId = await GetOrganizationIdAsync();
        if (orgId == null) return Unauthorized();

        var (_, since) = ResolveRange(range);
        var rows = (await _repo.GetSentimentSummaryDataAsync(orgId.Value, since)).ToList();

        if (rows.Count == 0) return Ok(new { hasData = false, positivePct = 0, neutralPct = 0, negativePct = 0, quotes = Array.Empty<object>() });

        var total = rows.Count;
        var positivePct = Math.Round((double)rows.Count(r => r.Sentiment == "pos") / total * 100, 1);
        var neutralPct = Math.Round((double)rows.Count(r => r.Sentiment == "neu") / total * 100, 1);
        var negativePct = Math.Round((double)rows.Count(r => r.Sentiment == "neg") / total * 100, 1);

        var quotes = rows
            .Where(r => !string.IsNullOrWhiteSpace(r.SentimentQuote))
            .OrderByDescending(r => r.RunAt)
            .Take(12)
            .Select(r => new { quote = r.SentimentQuote, sentiment = r.Sentiment, platform = r.Platform, runAt = r.RunAt.ToString("yyyy-MM-dd") });

        return Ok(new { hasData = true, positivePct, neutralPct, negativePct, quotes });
    }

    [HttpGet("citations-summary")]
    public async Task<IActionResult> GetCitationsSummary([FromQuery] string range = "30D")
    {
        var orgId = await GetOrganizationIdAsync();
        if (orgId == null) return Unauthorized();

        var (_, since) = ResolveRange(range);
        var rows = (await _repo.GetCitationSummaryDataAsync(orgId.Value, since)).ToList();

        if (rows.Count == 0)
        {
            return Ok(new { hasData = false, topDomains = Array.Empty<object>(), categories = Array.Empty<object>(), topPages = Array.Empty<object>() });
        }

        var total = rows.Count;

        var topDomains = rows
            .GroupBy(r => r.Domain)
            .Select(g => new { domain = g.Key, category = g.First().Category, share = Math.Round((double)g.Count() / total * 100, 1) })
            .OrderByDescending(d => d.share)
            .Take(10)
            .ToList();

        var categories = rows
            .GroupBy(r => r.Category)
            .Select(g => new { category = g.Key, share = Math.Round((double)g.Count() / total * 100, 1) })
            .OrderByDescending(c => c.share)
            .ToList();

        var topPages = rows
            .GroupBy(r => r.Url)
            .Select(g => new
            {
                url = g.Key,
                domain = g.First().Domain,
                category = g.First().Category,
                share = Math.Round((double)g.Count() / total * 100, 1),
                firstSeen = g.Min(r => r.RunAt).ToString("yyyy-MM-dd"),
            })
            .OrderByDescending(p => p.share)
            .Take(15)
            .ToList();

        return Ok(new { hasData = true, topDomains, categories, topPages });
    }

    /// <summary>
    /// Query Fanouts tab overview table. avgQueriesPerExecution is a real derived metric —
    /// stored fanout variations per question divided by how many completed analysis runs that
    /// question actually has — not a per-execution telemetry stream (fanouts are generated
    /// on-demand, not regenerated every analysis run).
    /// </summary>
    [HttpGet("fanouts-overview")]
    public async Task<IActionResult> GetFanoutsOverview()
    {
        var orgId = await GetOrganizationIdAsync();
        if (orgId == null) return Unauthorized();

        var rows = (await _repo.GetFanoutOverviewDataAsync(orgId.Value))
            .Select(r => new
            {
                questionId = r.QuestionId,
                promptText = r.PromptText,
                fanoutCount = r.FanoutCount,
                avgQueriesPerExecution = Math.Round((double)r.FanoutCount / Math.Max(1, r.AnalysisCount), 1),
            })
            .OrderByDescending(r => r.fanoutCount)
            .ToList();

        return Ok(new { hasData = rows.Count > 0, prompts = rows });
    }

    [HttpGet("questions/{questionId}/fanouts")]
    public async Task<IActionResult> GetFanouts(Guid questionId)
    {
        var orgId = await GetOrganizationIdAsync();
        if (orgId == null) return Unauthorized();

        var topic = await GetOwningTopicForQuestionAsync(questionId, orgId.Value);
        if (topic == null) return NotFound();

        var fanouts = await _repo.GetFanoutsByQuestionAsync(questionId);
        return Ok(fanouts);
    }

    [HttpPost("questions/{questionId}/fanouts/generate")]
    public async Task<IActionResult> GenerateFanouts(Guid questionId, CancellationToken ct)
    {
        var orgId = await GetOrganizationIdAsync();
        if (orgId == null) return Unauthorized();

        var topic = await GetOwningTopicForQuestionAsync(questionId, orgId.Value);
        if (topic == null) return NotFound();

        var question = await _repo.GetQuestionAsync(questionId);
        if (question == null) return NotFound();

        var fanouts = await _fanoutService.GenerateFanoutsAsync(questionId, question.PromptText, ct);
        if (fanouts.Count == 0)
        {
            return StatusCode(502, new { error = "Fanout generation failed — the AI service didn't return usable results. Try again." });
        }

        await _repo.DeleteFanoutsByQuestionAsync(questionId);
        await _repo.InsertFanoutsAsync(fanouts);

        var saved = await _repo.GetFanoutsByQuestionAsync(questionId);
        return Ok(saved);
    }

    [HttpPost("topics/{topicId}/generate-prompts")]
    public async Task<IActionResult> GenerateTopicPrompts(Guid topicId, [FromBody] GeneratePromptsRequest request, CancellationToken ct)
    {
        var orgId = await GetOrganizationIdAsync();
        if (orgId == null) return Unauthorized();

        var topic = await _repo.GetTopicAsync(topicId);
        if (topic == null || topic.OrganizationId != orgId.Value) return NotFound();

        // Pass brand context so generated prompts are niche-relevant (not generic)
        var profile = await _websiteRepository.GetLatestWebsiteProfileAsync(orgId.Value);

        var count = Math.Clamp(request.Count, 1, 20);
        var generatedTexts = await _topicPromptGenerator.GeneratePromptsAsync(
            topicId, topic.Name, count, ct,
            brandName: profile?.BusinessName,
            brandWebsite: profile?.WebsiteUrl);

        if (generatedTexts.Count == 0)
        {
            return StatusCode(502, new { error = "Prompt generation failed — the AI service didn't return usable results. Try again." });
        }

        var created = new List<PromptQuestion>();
        foreach (var text in generatedTexts)
        {
            var question = new PromptQuestion { PromptTopicId = topicId, PromptText = text };
            question.Id = await _repo.CreateQuestionAsync(question);
            created.Add(question);
        }

        return Ok(created);
    }

    [HttpGet("questions/{questionId}/history")]
    public async Task<IActionResult> GetQuestionHistory(Guid questionId)
    {
        var orgId = await GetOrganizationIdAsync();
        if (orgId == null) return Unauthorized();

        var topic = await GetOwningTopicForQuestionAsync(questionId, orgId.Value);
        if (topic == null) return NotFound();

        var history = await _repo.GetExecutionHistoryAsync(questionId);
        return Ok(history);
    }
}

public class UpdateQuestionRequest
{
    public string? PromptText { get; set; }
    public bool? IsActive { get; set; }
}

public class GeneratePromptsRequest
{
    public int Count { get; set; } = 8;
}

file class RankEntity
{
    public string Name { get; set; } = string.Empty;
    public bool Owned { get; set; }
    public double Value { get; set; }
    public double OlderValue { get; set; }
    public double RecentValue { get; set; }
}
