using System.Collections.Generic;

namespace Citationly.Application.Features.AnswerSimulator;

public class SimulateAnswerRequest
{
    public string Prompt { get; set; } = string.Empty;
    public string Persona { get; set; } = string.Empty;
    public string Stage { get; set; } = string.Empty;
    public string Region { get; set; } = string.Empty;
    public string Brand { get; set; } = string.Empty;
}

public class CompetitorShare
{
    public string Name { get; set; } = string.Empty;
    public int SharePct { get; set; }
}

public class SourceReference
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = "third"; // "you" | "comp" | "third"
}

public class SimulateAnswerResponse
{
    public string Answer { get; set; } = string.Empty;
    public bool Mentioned { get; set; }
    public string Position { get; set; } = "Not mentioned";
    public string Sentiment { get; set; } = "neu"; // pos | neu | neg
    public int SharePct { get; set; }
    public List<CompetitorShare> Competitors { get; set; } = new();
    public List<SourceReference> Sources { get; set; } = new();
    public string Summary { get; set; } = string.Empty;

    /// <summary>Deterministically derived from Mentioned/SharePct — how many of 5 hypothetical
    /// re-runs would likely still surface the brand — not a literal 5x re-query.</summary>
    public int ConsistencyOutOfFive { get; set; }
}

public class CompareContentRequest
{
    public string Prompt { get; set; } = string.Empty;
    public string Brand { get; set; } = string.Empty;
    public string PageContent { get; set; } = string.Empty;
}

public class CompareContentResponse
{
    public string Without { get; set; } = string.Empty;
    public string With { get; set; } = string.Empty;
    public bool Changed { get; set; }
    public string Verdict { get; set; } = string.Empty;
}

public class BattleRequest
{
    public string Prompt { get; set; } = string.Empty;
    public string Brand { get; set; } = string.Empty;
    public string Competitor { get; set; } = string.Empty;
}

public class BattleResponse
{
    public int YouPct { get; set; }
    public int CompPct { get; set; }
    public string Note { get; set; } = string.Empty;
}
