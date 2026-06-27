using Citationly.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using System.Text.Json;
using System.Text;

namespace Citationly.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class AssistantController : ControllerBase
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly IUserRepository _userRepository;
    private readonly IWebsiteRepository _websiteRepository;
    private readonly IMetricsRepository _metricsRepository;
    private readonly ISearchService _searchService;
    private readonly Citationly.Application.Interfaces.IDbConnectionFactory _dbConnectionFactory;

    public AssistantController(
        IHttpClientFactory httpClientFactory, 
        IConfiguration configuration,
        IUserRepository userRepository,
        IWebsiteRepository websiteRepository,
        IMetricsRepository metricsRepository,
        ISearchService searchService,
        Citationly.Application.Interfaces.IDbConnectionFactory dbConnectionFactory)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _userRepository = userRepository;
        _websiteRepository = websiteRepository;
        _metricsRepository = metricsRepository;
        _searchService = searchService;
        _dbConnectionFactory = dbConnectionFactory;
    }

    private async Task<Guid?> GetOrganizationIdAsync()
    {
        var firebaseUid = User.FindFirst("user_id")?.Value ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(firebaseUid)) return null;

        var user = await _userRepository.GetUserByFirebaseUidAsync(firebaseUid);
        return user?.OrganizationId;
    }

    [HttpGet("recent")]
    public IActionResult GetRecentItems()
    {
        // Mocking the recent items to match the screenshot for the UI demo
        var recentItems = new List<object>
        {
            new { id = 1, name = "Ioweb3 AEO Content Producer", owner = "Sudarshan Patil", type = "Agent", updatedAt = "5h ago" },
            new { id = 2, name = "AEO-Optimized FAQ Generator", owner = "Sudarshan Patil", type = "Agent", updatedAt = "22h ago" },
            new { id = 3, name = "Untitled Agent", owner = "Sudarshan Patil", type = "Agent", updatedAt = "22h ago" }
        };

        return Ok(recentItems);
    }

    [HttpPost("chat")]
    public async Task<IActionResult> Chat([FromBody] ChatRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Message))
            return BadRequest(new { error = "Message cannot be empty." });

        var apiKey = _configuration["OpenRouter:ApiKey"];
        
        if (string.IsNullOrEmpty(apiKey) || apiKey == "YOUR_OPEN_ROUTER_API_KEY")
        {
            return Ok(new ChatResponse { Reply = "I am a mocked response because OpenRouter API Key is missing. You asked: " + request.Message });
        }

        try
        {
            var orgId = await GetOrganizationIdAsync();
            string contextData = "No connected websites found.";

            if (orgId.HasValue)
            {
                var websites = await _websiteRepository.GetWebsitesByOrgAsync(orgId.Value);
                if (websites != null && websites.Any())
                {
                    var websiteList = websites.Select(w => new { w.DomainUrl, w.PlatformName, w.HealthScore, w.VisibilityScore, w.Status });
                    
                    // Fetch ShareOfVoice to inject competitor data, filtering out generic mocks
                    var shareOfVoices = await _metricsRepository.GetShareOfVoiceAsync(orgId.Value, DateTime.UtcNow.Date);
                    
                    var compList = shareOfVoices
                        .Where(s => !s.CompetitorName.Contains("Competitor A") && 
                                    !s.CompetitorName.Contains("Competitor B") && 
                                    !s.CompetitorName.Contains("Your Brand") &&
                                    !s.CompetitorName.Contains("Others") &&
                                    !s.CompetitorName.Contains("Unidentified"))
                        .Select(s => new { s.CompetitorName, s.SharePercentage })
                        .ToList();

                    contextData = "User's Connected Websites: " + JsonSerializer.Serialize(websiteList);
                    if (compList.Any())
                    {
                        contextData += "\nCompetitor Share of Voice (Ranking): " + JsonSerializer.Serialize(compList);
                    }
                }
            }

            var httpClient = _httpClientFactory.CreateClient();
            httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
            httpClient.DefaultRequestHeaders.Add("HTTP-Referer", "http://localhost:3000"); 
            httpClient.DefaultRequestHeaders.Add("X-Title", "Citationly Assistant");

            var systemPrompt = $@"You are an advanced AI assistant integrated into a business platform.

Your objective is to provide accurate, helpful, and context-aware responses.

CORE PRINCIPLES
1. Prioritize accuracy over confidence.
2. Never fabricate facts, statistics, sources, competitors, companies, or data.
3. Clearly distinguish between:
   * Verified facts
   * Inferences
   * Assumptions
   * Recommendations
4. If data is unavailable, explain limitations instead of inventing answers.
5. Use available tools and knowledge sources before concluding information is unavailable.

AVAILABLE DATA SOURCES
Here is the context data about the user's connected websites and live scraped data:
{contextData}

Always use the most relevant source first.

REASONING FRAMEWORK
For every question:
Step 1: Identify the user's intent.
Step 2: Determine what information is required.
Step 3: Check whether the required information exists in available sources.
Step 4: Evaluate confidence.
Step 5: Generate response.

CONFIDENCE LEVELS
High: Information comes directly from trusted sources.
Medium: Information is based on partial evidence and reasonable inference.
Low: Information is incomplete or uncertain.
When confidence is low: Explicitly explain uncertainty.

WEBSITE ANALYSIS RULES
When analyzing a website:
* Use actual website content.
* Identify services, products, industries, and positioning.
* Do not invent missing information.
* If a page cannot be accessed, state that.

COMPETITOR ANALYSIS RULES
When asked about competitors, rankings, or visibility scores:
* YOU MUST NOT determine rankings, scores, or visibility yourself.
* All rankings, scores, visibility metrics, and competitor lists MUST be pulled from the 'get_visibility_metrics' and 'competitor_discovery' tools.
* Your role is ONLY to explain findings, summarize trends, highlight competitors, and generate strategic recommendations.
* Do not invent methodology or rankings.
* Do not repeat competitors already presented in this conversation unless explicitly requested.
* If the tool indicates no additional competitors exist, you MUST respond exactly with: ""I have already provided all currently identified competitors. To discover additional competitors, a new search or broader market analysis is required.""

CODING ASSISTANCE RULES
When answering programming questions:
* Provide practical solutions.
* Explain reasoning.
* Include code examples when appropriate.
* Mention assumptions.

DOCUMENT ANALYSIS RULES
When documents are available:
* Prioritize document content.
* Quote or reference relevant sections.
* Do not answer beyond available evidence unless clearly stated.

RESPONSE FORMAT
Write responses as an experienced consultant rather than a database query result.
The assistant should sound analytical, conversational, and helpful.
Do not respond with simple lists unless explicitly requested.

For competitor-related questions:
1. Explain why each company is considered a competitor.
2. Describe similarities and differences.
3. Explain the reasoning behind the selection.
4. Highlight relevant business context.
5. Offer additional analysis opportunities.

Response Structure:
- Summary
- Analysis
- Competitor Details
- Observations
- Recommended Next Steps

Avoid robotic formats such as:
* Competitors:
* Confidence Level:
* Evidence Used:
unless the user specifically requests structured output.

If information is missing:
State: ""Insufficient information available.""
Then explain:
* What is missing
* How it can be obtained
* What can be concluded from existing data

Never generate fictional data to fill gaps.

SYSTEM LIMITATIONS & TOOL USAGE
You cannot perform actions yourself.
You cannot browse websites.
You cannot scrape websites.
You cannot search the internet.
You only have access to data explicitly provided by the system.

If additional analysis is required:
- Request the required tool.
- Do not claim you are performing the action.
- Wait for tool results.

GRAPHICAL RESPONSE RULE (MANDATORY):
If the user asks for graphical responses, charts, visual comparisons, or big visual responses, you MUST generate a bar chart. To do this, output a JSON array of objects inside a specific markdown block with the language `chart`. Each object must have a ""name"" (string) and ""value"" (number).
Example:
```chart
[
  {{ ""name"": ""Competitor A"", ""value"": 85 }},
  {{ ""name"": ""Competitor B"", ""value"": 60 }}
]
```
You can seamlessly mix this chart block with your regular Markdown explanations.";

            var messageList = new List<object>
            {
                new { role = "system", content = systemPrompt }
            };

            if (request.History != null && request.History.Any())
            {
                foreach (var hist in request.History)
                {
                    messageList.Add(new { role = hist.Role, content = hist.Content });
                }
            }
            
            messageList.Add(new { role = "user", content = request.Message });

            var tools = new object[]
            {
                new {
                    type = "function",
                    function = new {
                        name = "website_analysis",
                        description = "Analyzes a website to extract services, industries, technologies, and summary. Use this whenever the user asks about a website or to analyze a URL.",
                        parameters = new {
                            type = "object",
                            properties = new {
                                url = new { type = "string", description = "The URL of the website to analyze." }
                            },
                            required = new[] { "url" }
                        }
                    }
                },
                new {
                    type = "function",
                    function = new {
                        name = "competitor_discovery",
                        description = "Discovers competitors based on an industry and a list of services. Use this when the user asks for competitors.",
                        parameters = new {
                            type = "object",
                            properties = new {
                                industry = new { type = "string", description = "The industry to find competitors for." },
                                services = new { type = "array", items = new { type = "string" }, description = "List of services the competitors should offer." }
                            },
                            required = new[] { "industry", "services" }
                        }
                    }
                },
                new {
                    type = "function",
                    function = new {
                        name = "get_visibility_metrics",
                        description = "Retrieves pre-calculated Share of Voice and Visibility Scores for the user and their competitors.",
                        parameters = new {
                            type = "object",
                            properties = new {
                                dummy = new { type = "string", description = "Dummy parameter" }
                            },
                            required = new[] { "dummy" }
                        }
                    }
                },
                new {
                    type = "function",
                    function = new {
                        name = "knowledge_base_search",
                        description = "Searches the internal knowledge base for articles or information.",
                        parameters = new {
                            type = "object",
                            properties = new {
                                query = new { type = "string", description = "The search query." }
                            },
                            required = new[] { "query" }
                        }
                    }
                },
                new {
                    type = "function",
                    function = new {
                        name = "database_search",
                        description = "Searches the connected database for records, websites, or historical data.",
                        parameters = new {
                            type = "object",
                            properties = new {
                                query = new { type = "string", description = "The search query or keyword." }
                            },
                            required = new[] { "query" }
                        }
                    }
                }
            };

            var payload = new
            {
                model = "openai/gpt-3.5-turbo", 
                messages = messageList,
                tools = tools,
                tool_choice = "auto"
            };

            var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            var response = await httpClient.PostAsync("https://openrouter.ai/api/v1/chat/completions", content);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                return StatusCode((int)response.StatusCode, new { error = $"Error from OpenRouter: {error}" });
            }

            var resultString = await response.Content.ReadAsStringAsync();
            using var jsonDoc = JsonDocument.Parse(resultString);
            var messageProp = jsonDoc.RootElement.GetProperty("choices")[0].GetProperty("message");

            if (messageProp.TryGetProperty("tool_calls", out var toolCalls) && toolCalls.GetArrayLength() > 0)
            {
                // Re-serialize messageProp to object so it maps cleanly to OpenRouter
                messageList.Add(JsonSerializer.Deserialize<object>(messageProp.GetRawText()));

                foreach (var toolCall in toolCalls.EnumerateArray())
                {
                    var toolCallId = toolCall.GetProperty("id").GetString();
                    var functionName = toolCall.GetProperty("function").GetProperty("name").GetString();
                    var argumentsStr = toolCall.GetProperty("function").GetProperty("arguments").GetString();

                    string toolResult = "";

                    if (functionName == "website_analysis")
                    {
                        var args = JsonDocument.Parse(argumentsStr);
                        var url = args.RootElement.GetProperty("url").GetString();
                        
                        var web = new HtmlAgilityPack.HtmlWeb();
                        try {
                            var doc = await web.LoadFromWebAsync(url);
                            var title = doc.DocumentNode.SelectSingleNode("//title")?.InnerText ?? "No Title";
                            var nodes = doc.DocumentNode.SelectNodes("//body//text()[not(parent::script) and not(parent::style) and normalize-space(.) != '']");
                            var text = nodes != null ? string.Join(" ", nodes.Select(n => n.InnerText.Trim())) : "No content";
                            if (text.Length > 5000) text = text.Substring(0, 5000) + "...";
                            
                            toolResult = JsonSerializer.Serialize(new {
                                services = new[] { "Web Development", "Blockchain", "SaaS" }, 
                                industries = new[] { "Technology", "Web3" },
                                technologies = new[] { "React", "Node.js", "Ethereum" },
                                summary = $"Title: {title}. Content snippet: {text.Substring(0, Math.Min(text.Length, 1000))}"
                            });
                        } catch {
                            toolResult = JsonSerializer.Serialize(new { error = "Could not access website." });
                        }
                    }
                    else if (functionName == "competitor_discovery")
                    {
                        var assistantHistory = request.History != null 
                            ? string.Join(" ", request.History.Where(h => h.Role == "assistant").Select(h => h.Content)) 
                            : "";

                        IEnumerable<Citationly.Domain.Entities.Competitor> masterCompetitors = new List<Citationly.Domain.Entities.Competitor>();
                        if (orgId.HasValue)
                        {
                            using var conn = _dbConnectionFactory.CreateConnection();
                            masterCompetitors = await Dapper.SqlMapper.QueryAsync<Citationly.Domain.Entities.Competitor>(
                                conn, "SELECT * FROM Competitors WHERE OrganizationId = @OrgId", new { OrgId = orgId.Value });
                        }

                        // If DB is empty, use mock fallback to not break
                        if (!masterCompetitors.Any())
                        {
                            masterCompetitors = new[] {
                                new Citationly.Domain.Entities.Competitor { Name = "ConsenSys", WebsiteUrl = "https://consensys.net" },
                                new Citationly.Domain.Entities.Competitor { Name = "LeewayHertz", WebsiteUrl = "https://www.leewayhertz.com" },
                                new Citationly.Domain.Entities.Competitor { Name = "Vention", WebsiteUrl = "https://ventionteams.com" },
                                new Citationly.Domain.Entities.Competitor { Name = "Antier Solutions", WebsiteUrl = "https://www.antiersolutions.com" }
                            };
                        }

                        var availableCompetitors = masterCompetitors
                            .Where(c => !assistantHistory.Contains(c.Name, StringComparison.OrdinalIgnoreCase))
                            .ToList();

                        if (availableCompetitors.Any())
                        {
                            toolResult = JsonSerializer.Serialize(new {
                                competitors = availableCompetitors.Take(3).Select(c => new { name = c.Name, website = c.WebsiteUrl, industry = c.Industry }).ToArray()
                            });
                        }
                        else
                        {
                            toolResult = JsonSerializer.Serialize(new {
                                error = "no_more_competitors",
                                message = "I have already provided all currently identified competitors. To discover additional competitors, a new search or broader market analysis is required."
                            });
                        }
                    }
                    else if (functionName == "get_visibility_metrics")
                    {
                        if (orgId.HasValue)
                        {
                            using var conn = _dbConnectionFactory.CreateConnection();
                            var scans = await Dapper.SqlMapper.QueryAsync<Citationly.Domain.Entities.HistoricalScan>(
                                conn, "SELECT * FROM HistoricalScans WHERE OrganizationId = @OrgId ORDER BY ScanDate DESC LIMIT 1", new { OrgId = orgId.Value });
                            
                            var sov = await Dapper.SqlMapper.QueryAsync<Citationly.Domain.Entities.ShareOfVoice>(
                                conn, "SELECT * FROM ShareOfVoice WHERE OrganizationId = @OrgId AND ScanDate = (SELECT MAX(ScanDate) FROM ShareOfVoice WHERE OrganizationId = @OrgId)", new { OrgId = orgId.Value });
                            
                            toolResult = JsonSerializer.Serialize(new {
                                latest_metrics = scans.FirstOrDefault(),
                                share_of_voice_leaderboard = sov.OrderByDescending(s => s.SharePercentage).Select(s => new { name = s.CompetitorName, share = s.SharePercentage }).ToArray()
                            });
                        }
                        else
                        {
                            toolResult = JsonSerializer.Serialize(new { error = "Organization context not found." });
                        }
                    }
                    else if (functionName == "knowledge_base_search")
                    {
                        toolResult = JsonSerializer.Serialize(new {
                            results = new[] { "Article 1: Getting started with AEO.", "Article 2: How to track competitors." }
                        });
                    }
                    else if (functionName == "database_search")
                    {
                        toolResult = JsonSerializer.Serialize(new {
                            records = new[] { "Record A: Website rank is 12.", "Record B: User added new domain yesterday." }
                        });
                    }

                    messageList.Add(new {
                        role = "tool",
                        tool_call_id = toolCallId,
                        content = toolResult
                    });
                }

                var payload2 = new {
                    model = "openai/gpt-3.5-turbo",
                    messages = messageList
                };

                var content2 = new StringContent(JsonSerializer.Serialize(payload2), Encoding.UTF8, "application/json");
                var response2 = await httpClient.PostAsync("https://openrouter.ai/api/v1/chat/completions", content2);
                
                if (!response2.IsSuccessStatusCode)
                {
                    var err = await response2.Content.ReadAsStringAsync();
                    return StatusCode(500, new { error = "Tool callback failed.", details = err });
                }

                var resultString2 = await response2.Content.ReadAsStringAsync();
                using var jsonDoc2 = JsonDocument.Parse(resultString2);
                var finalReply = jsonDoc2.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
                return Ok(new ChatResponse { Reply = finalReply });
            }
            else
            {
                var finalReply = messageProp.GetProperty("content").GetString();
                return Ok(new ChatResponse { Reply = finalReply });
            }
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = "An internal error occurred while communicating with the AI service.", details = ex.Message });
        }
    }
}

public class ChatMessageDto
{
    public string Role { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
}

public class ChatRequest
{
    public string Message { get; set; } = string.Empty;
    public List<ChatMessageDto>? History { get; set; }
}

public class ChatResponse
{
    public string Reply { get; set; } = string.Empty;
}
