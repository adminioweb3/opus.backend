namespace Citationly.Application.Dtos;

// ── Score (existing shape, now typed) ───────────────────────────
public record ScoreEntryDto(int Value, string Change, string Direction);

public record ScoreCardDto(
    ScoreEntryDto VisibilityScore,
    ScoreEntryDto CitationScore,
    ScoreEntryDto SentimentScore,
    ScoreEntryDto CompetitorScore,
    ScoreEntryDto HallucinationRisk,
    ScoreEntryDto SeoHealth,
    ScoreEntryDto AeoReadiness,
    ScoreEntryDto GeoReadiness);

// ── Header ──────────────────────────────────────────────────────
public record GeoDashboardHeaderDto(
    int CompositeScore,
    string Grade,
    int IndustryAverage,
    int DeltaVsIndustry,
    string CompositeChange,
    int EnginesScanned,
    int PromptsTracked,
    string Status);

// ── Pillar ──────────────────────────────────────────────────────
public record GeoPillarDto(
    string Key,
    string Label,
    string Description,
    int Score);

// ── Weakest-Pillar Insight (derived) ────────────────────────────
public record WeakestPillarInsightDto(
    string PillarKey,
    int Score,
    string Message,
    string CtaLabel,
    string CtaLink);

// ── Prompt-Type Coverage ────────────────────────────────────────
public record PromptTypeCoverageDto(
    string Type,
    string Example,
    string Note,
    int Percentage,
    string Direction);

// ── Opportunity Insight (derived) ───────────────────────────────
public record OpportunityInsightDto(
    string Message,
    string CtaLabel,
    string CtaLink);

// ── Win / Loss ──────────────────────────────────────────────────
public record WinLossEventDto(
    string Type,
    string Title,
    string Engine,
    string Timestamp);

// ── Verify Insight (static) ─────────────────────────────────────
public record VerifyInsightDto(
    string Message,
    string CtaLabel,
    string CtaLink);

// ── Trend point ─────────────────────────────────────────────────
public record TrendPointDto(int Day, int Score);

// ── Share-of-voice entry ────────────────────────────────────────
public record ShareOfVoiceEntryDto(string Name, double Value, string Color);

// ── Full GEO Dashboard response ─────────────────────────────────
public record GeoDashboardDto(
    bool HasData,
    ScoreCardDto Scores,
    List<TrendPointDto> Trend,
    List<ShareOfVoiceEntryDto> ShareOfVoice,
    GeoDashboardHeaderDto Header,
    List<GeoPillarDto> Pillars,
    WeakestPillarInsightDto? WeakestPillarInsight,
    List<PromptTypeCoverageDto> PromptTypeCoverage,
    OpportunityInsightDto? OpportunityInsight,
    List<WinLossEventDto> WinsAndLosses,
    VerifyInsightDto VerifyInsight);
