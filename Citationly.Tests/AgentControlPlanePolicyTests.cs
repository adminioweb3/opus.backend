using Citationly.Application.Features.Assistant.Agents;
using Citationly.Domain.Entities;
using Xunit;

namespace Citationly.Tests;

public sealed class AgentControlPlanePolicyTests
{
    [Theory]
    [InlineData(CitationlyAgentKeys.VisibilityMonitor)]
    [InlineData(CitationlyAgentKeys.IntelligenceAnalyst)]
    [InlineData(CitationlyAgentKeys.GeoStrategy)]
    [InlineData(CitationlyAgentKeys.ContentExecution)]
    [InlineData(CitationlyAgentKeys.ImpactReporting)]
    public void KnownAgentCatalog_AcceptsEverySeededAgent(string agentKey)
    {
        Assert.True(AgentControlPlanePolicy.IsKnownAgent(agentKey));
    }

    [Fact]
    public void KnownAgentCatalog_RejectsUnknownAgent()
    {
        Assert.False(AgentControlPlanePolicy.IsKnownAgent("unbounded-general-agent"));
    }

    [Theory]
    [InlineData(AgentAutonomyLevels.Observe)]
    [InlineData(AgentAutonomyLevels.Assist)]
    [InlineData(AgentAutonomyLevels.Autopilot)]
    public void AutonomyValidation_AcceptsSupportedLevels(string autonomyLevel)
    {
        Assert.True(AgentControlPlanePolicy.IsValidAutonomyLevel(autonomyLevel));
    }

    [Theory]
    [InlineData(AgentRunStatuses.Queued, true)]
    [InlineData(AgentRunStatuses.Running, true)]
    [InlineData(AgentRunStatuses.WaitingForApproval, true)]
    [InlineData(AgentRunStatuses.CancellationRequested, true)]
    [InlineData(AgentRunStatuses.Completed, false)]
    [InlineData(AgentRunStatuses.Failed, false)]
    [InlineData(AgentRunStatuses.Cancelled, false)]
    public void CancellationPolicy_OnlyAllowsActiveRuns(string status, bool expected)
    {
        Assert.Equal(expected, AgentControlPlanePolicy.CanCancelRun(status));
    }

    [Theory]
    [InlineData(AgentRunStatuses.Failed, 1, 3, true)]
    [InlineData(AgentRunStatuses.Cancelled, 2, 3, true)]
    [InlineData(AgentRunStatuses.Failed, 3, 3, false)]
    [InlineData(AgentRunStatuses.Completed, 1, 3, false)]
    public void RetryPolicy_RequiresFailedOrCancelledRunBelowAttemptLimit(string status, int attempt, int maxAttempts, bool expected)
    {
        Assert.Equal(expected, AgentControlPlanePolicy.CanRetryRun(status, attempt, maxAttempts));
    }

    [Theory]
    [InlineData(AgentAutonomyLevels.Autopilot, "Low", false, false)]
    [InlineData(AgentAutonomyLevels.Assist, "Low", false, true)]
    [InlineData(AgentAutonomyLevels.Autopilot, "High", false, true)]
    [InlineData(AgentAutonomyLevels.Autopilot, "Low", true, true)]
    public void ApprovalPolicy_NeverBypassesExternalOrHighRiskActions(string autonomyLevel, string riskLevel, bool externalSideEffect, bool expected)
    {
        Assert.Equal(expected, AgentControlPlanePolicy.RequiresApproval(autonomyLevel, riskLevel, externalSideEffect));
    }

    [Theory]
    [InlineData(AgentRecommendationStatuses.Approved, AgentRecommendationStatuses.InProgress, true)]
    [InlineData(AgentRecommendationStatuses.Assigned, AgentRecommendationStatuses.InProgress, true)]
    [InlineData(AgentRecommendationStatuses.InProgress, AgentRecommendationStatuses.Implemented, true)]
    [InlineData(AgentRecommendationStatuses.AwaitingApproval, AgentRecommendationStatuses.InProgress, false)]
    [InlineData(AgentRecommendationStatuses.Approved, AgentRecommendationStatuses.Implemented, false)]
    [InlineData(AgentRecommendationStatuses.Implemented, AgentRecommendationStatuses.Dismissed, false)]
    public void RecommendationTransitions_EnforceLifecycle(string current, string next, bool expected)
    {
        Assert.Equal(expected, AgentControlPlanePolicy.CanTransitionRecommendation(current, next));
    }
}
