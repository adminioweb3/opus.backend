using Citationly.Domain.Entities;

namespace Citationly.Application.Features.Assistant.Agents;

public static class AgentControlPlanePolicy
{
    private static readonly IReadOnlySet<string> ApprovalDecisions = new HashSet<string>(StringComparer.Ordinal)
    {
        AgentApprovalStatuses.Approved,
        AgentApprovalStatuses.Rejected
    };

    public static bool IsKnownAgent(string agentKey) => CitationlyAgentKeys.All.Contains(agentKey);

    public static bool IsValidAutonomyLevel(string autonomyLevel) => AgentAutonomyLevels.All.Contains(autonomyLevel);

    public static bool IsValidApprovalDecision(string decision) => ApprovalDecisions.Contains(decision);

    public static bool CanAssignRecommendation(string status) => status is
        AgentRecommendationStatuses.Approved or AgentRecommendationStatuses.Assigned;

    public static bool CanTransitionRecommendation(string currentStatus, string nextStatus) =>
        nextStatus switch
        {
            AgentRecommendationStatuses.InProgress => currentStatus is
                AgentRecommendationStatuses.Approved or AgentRecommendationStatuses.Assigned,
            AgentRecommendationStatuses.Implemented => currentStatus == AgentRecommendationStatuses.InProgress,
            AgentRecommendationStatuses.Dismissed => currentStatus is
                AgentRecommendationStatuses.Approved or AgentRecommendationStatuses.Assigned or AgentRecommendationStatuses.InProgress,
            _ => false
        };

    public static bool CanCancelRun(string status) => status is
        AgentRunStatuses.Queued or
        AgentRunStatuses.Running or
        AgentRunStatuses.WaitingForApproval or
        AgentRunStatuses.CancellationRequested;

    public static bool CanRetryRun(string status, int attempt, int maxAttempts) =>
        status is AgentRunStatuses.Failed or AgentRunStatuses.Cancelled
        && attempt < maxAttempts;

    public static bool RequiresApproval(string autonomyLevel, string riskLevel, bool hasExternalSideEffect)
    {
        if (hasExternalSideEffect) return true;
        if (riskLevel is "High" or "Critical") return true;
        return autonomyLevel != AgentAutonomyLevels.Autopilot;
    }
}
