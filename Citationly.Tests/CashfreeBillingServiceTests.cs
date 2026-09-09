using System.Net;
using System.Net.Http.Json;
using Citationly.Application.Interfaces;
using Citationly.Domain.Entities;
using Citationly.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Citationly.Tests;

public class CashfreeBillingServiceTests
{
    [Fact]
    public async Task ReconcileSubscriptionsAsync_RepairsLocalPlanState_WhenWebhookWasMissed()
    {
        var organizationId = Guid.NewGuid();
        var repository = new StubBillingRepository();
        repository.Subscriptions.Add(new Subscription
        {
            OrganizationId = organizationId,
            CashfreeSubscriptionId = "sub_reconcile",
            PlanKey = "Pro",
            Status = "PENDING"
        });

        var service = new CashfreeBillingService(
            new StubHttpClientFactory(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { subscription_status = "ACTIVE" })
            }),
            CreateConfiguration(),
            repository,
            new BillingRedirectUrlValidator(CreateConfiguration()),
            new CashfreeWebhookSignatureVerifier(CreateConfiguration()),
            NullLogger<CashfreeBillingService>.Instance);

        var reconciled = await service.ReconcileSubscriptionsAsync();

        Assert.Equal(1, reconciled);
        Assert.Equal("ACTIVE", repository.Subscriptions[0].Status);
        Assert.Equal("Pro", repository.SyncedPlans[organizationId]);
    }

    private static IConfiguration CreateConfiguration() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Cashfree:AppId"] = "test-app",
            ["Cashfree:SecretKey"] = "test-secret",
            ["Cashfree:Environment"] = "Sandbox",
            ["Cashfree:AllowedReturnOrigins:0"] = "https://app.example.test"
        })
        .Build();

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpResponseMessage _response;

        public StubHttpClientFactory(HttpResponseMessage response)
        {
            _response = response;
        }

        public HttpClient CreateClient(string name) => new(new StubHandler(_response));
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly HttpResponseMessage _response;

        public StubHandler(HttpResponseMessage response)
        {
            _response = response;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(_response);
        }
    }

    private sealed class StubBillingRepository : IBillingRepository
    {
        public List<Subscription> Subscriptions { get; } = [];
        public Dictionary<Guid, string> SyncedPlans { get; } = [];

        public Task<Subscription?> GetActiveSubscriptionAsync(Guid organizationId) => Task.FromResult<Subscription?>(null);
        public Task<IEnumerable<Invoice>> GetInvoicesAsync(Guid organizationId, int limit = 100) => Task.FromResult(Enumerable.Empty<Invoice>());
        public Task<IEnumerable<PaymentMethod>> GetPaymentMethodsAsync(Guid organizationId) => Task.FromResult(Enumerable.Empty<PaymentMethod>());
        public Task<string?> GetStripeCustomerIdAsync(Guid organizationId) => Task.FromResult<string?>(null);
        public Task SetStripeCustomerIdAsync(Guid organizationId, string stripeCustomerId) => Task.CompletedTask;
        public Task UpsertSubscriptionAsync(Subscription subscription) => Task.CompletedTask;
        public Task UpsertInvoiceAsync(Invoice invoice) => Task.CompletedTask;
        public Task UpsertPaymentMethodAsync(PaymentMethod paymentMethod) => Task.CompletedTask;
        public Task<Guid?> GetOrganizationIdByStripeCustomerIdAsync(string stripeCustomerId) => Task.FromResult<Guid?>(null);
        public Task<Guid?> GetOrganizationIdByCashfreeSubscriptionIdAsync(string cashfreeSubscriptionId) => Task.FromResult<Guid?>(null);
        public Task<Subscription?> GetCashfreeSubscriptionAsync(string cashfreeSubscriptionId) =>
            Task.FromResult(Subscriptions.FirstOrDefault(s => s.CashfreeSubscriptionId == cashfreeSubscriptionId));
        public Task<Subscription?> GetCurrentCashfreeSubscriptionAsync(Guid organizationId) =>
            Task.FromResult(Subscriptions.FirstOrDefault(s => s.OrganizationId == organizationId));
        public Task<IReadOnlyList<Subscription>> GetCashfreeSubscriptionsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Subscription>>(Subscriptions.ToList());
        public Task<IReadOnlyList<(Guid OrganizationId, string StripeCustomerId)>> GetOrganizationsWithStripeCustomersAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<(Guid OrganizationId, string StripeCustomerId)>>([]);
        public Task<bool> TryBeginWebhookEventAsync(string stripeEventId, string payloadHash, string eventType, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task CompleteWebhookEventAsync(string stripeEventId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task FailWebhookEventAsync(string stripeEventId, string failureReason, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<bool> TryBeginCashfreeWebhookEventAsync(string eventId, string payloadHash, string eventType, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task CompleteCashfreeWebhookEventAsync(string eventId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task FailCashfreeWebhookEventAsync(string eventId, string failureReason, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task UpsertCashfreeSubscriptionAsync(Subscription subscription)
        {
            var index = Subscriptions.FindIndex(s => s.CashfreeSubscriptionId == subscription.CashfreeSubscriptionId);
            if (index >= 0) Subscriptions[index] = subscription;
            else Subscriptions.Add(subscription);
            return Task.CompletedTask;
        }

        public Task SyncOrganizationPlanTypeAsync(Guid organizationId, string planKey)
        {
            SyncedPlans[organizationId] = planKey;
            return Task.CompletedTask;
        }
    }
}
