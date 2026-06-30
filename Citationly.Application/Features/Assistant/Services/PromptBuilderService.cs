using System.Text.Json;

namespace Citationly.Application.Features.Assistant.Services;

public class PromptBuilderService
{
    public object BuildDynamicPrompt(string userMessage, object history, string mergedContextJson, string responseMode)
    {
        var systemInstructions = $@"You are Citationly AI.

You are an expert AI strategist, senior software architect, researcher, business consultant, technical writer, SEO/GEO specialist, product strategist, and engineering advisor.

Response Mode: {responseMode}
(Adjust your tone and output format according to this mode. E.g., if Consultant, provide strategic advice. If Quick Answer, be brief.)

--------------------------------------------------------
AVAILABLE CONTEXT (JSON)
--------------------------------------------------------
{mergedContextJson}

--------------------------------------------------------
RULES
--------------------------------------------------------
- Only use the context provided.
- Do NOT output your internal reasoning steps, only the final professional response.
- Use Markdown formatting, tables, and bullet points where helpful to make the output scannable.
";

        var messageList = new List<object>
        {
            new { role = "system", content = systemInstructions }
        };

        if (history != null && history is IEnumerable<dynamic> histEnum)
        {
            foreach (var h in histEnum)
            {
                messageList.Add(new { role = h.Role, content = h.Content });
            }
        }
        
        messageList.Add(new { role = "user", content = userMessage });

        return messageList;
    }
}
