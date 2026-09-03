using System.Net;
using Citationly.Application.Interfaces.Security;
using Citationly.Infrastructure.Services.GeoAudit;
using Xunit;

namespace Citationly.Tests;

public class GeoTechnicalAuditUrlSafetyTests
{
    [Fact]
    public async Task AuditAsync_DoesNotFollowRedirectsToUnsafeInternalUrls()
    {
        var handler = new RecordingHandler(request =>
        {
            if (request.RequestUri?.Host == "public.example")
            {
                return new HttpResponseMessage(HttpStatusCode.Found)
                {
                    Headers = { Location = new Uri("http://127.0.0.1/admin") }
                };
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("<html><body>internal should never be fetched</body></html>")
            };
        });
        var service = new GeoTechnicalAuditService(new HttpClient(handler), new FakeUrlSafetyValidator());

        var result = await service.AuditAsync("https://public.example");

        Assert.Equal(0, result.OverallScore);
        Assert.DoesNotContain(handler.RequestedUrls, url => url.Contains("127.0.0.1", StringComparison.Ordinal));
        Assert.Equal(new[] { "https://public.example/" }, handler.RequestedUrls);
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

        public RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
        {
            _respond = respond;
        }

        public List<string> RequestedUrls { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestedUrls.Add(request.RequestUri?.ToString() ?? string.Empty);
            return Task.FromResult(_respond(request));
        }
    }

    private sealed class FakeUrlSafetyValidator : IOutboundUrlSafetyValidator
    {
        public Task<OutboundUrlSafetyResult> ValidateForHttpFetchAsync(
            string? url,
            bool allowMissingScheme = false,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return Task.FromResult(OutboundUrlSafetyResult.Blocked("URL is required."));
            }

            var candidate = url;
            if (allowMissingScheme && !candidate.Contains("://", StringComparison.Ordinal))
            {
                candidate = "https://" + candidate;
            }

            if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri))
            {
                return Task.FromResult(OutboundUrlSafetyResult.Blocked("URL must be absolute."));
            }

            if (uri.Host is "127.0.0.1" or "localhost")
            {
                return Task.FromResult(OutboundUrlSafetyResult.Blocked("Internal host names are not allowed."));
            }

            return Task.FromResult(OutboundUrlSafetyResult.Allowed(new UriBuilder(uri) { Fragment = string.Empty }.Uri.ToString()));
        }
    }
}
