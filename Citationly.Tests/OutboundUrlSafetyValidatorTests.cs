using Citationly.Application.Security;
using Xunit;

namespace Citationly.Tests;

public class OutboundUrlSafetyValidatorTests
{
    private readonly OutboundUrlSafetyValidator _validator = new();

    [Theory]
    [InlineData("file:///etc/passwd")]
    [InlineData("ftp://example.com/file.txt")]
    [InlineData("http://localhost")]
    [InlineData("http://127.0.0.1")]
    [InlineData("http://10.0.0.5")]
    [InlineData("http://172.16.0.1")]
    [InlineData("http://192.168.1.10")]
    [InlineData("http://169.254.169.254/latest/meta-data")]
    [InlineData("http://[::1]/")]
    [InlineData("http://[fe80::1]/")]
    public async Task ValidateForHttpFetchAsync_BlocksUnsafeTargets(string url)
    {
        var result = await _validator.ValidateForHttpFetchAsync(url);

        Assert.False(result.IsAllowed);
        Assert.NotNull(result.Reason);
    }

    [Fact]
    public async Task ValidateForHttpFetchAsync_AllowsPublicHttpLiteral()
    {
        var result = await _validator.ValidateForHttpFetchAsync("http://93.184.216.34/path#fragment");

        Assert.True(result.IsAllowed);
        Assert.Equal("http://93.184.216.34/path", result.NormalizedUrl);
    }

    [Fact]
    public async Task ValidateForHttpFetchAsync_CanNormalizeMissingScheme()
    {
        var result = await _validator.ValidateForHttpFetchAsync("93.184.216.34/path", allowMissingScheme: true);

        Assert.True(result.IsAllowed);
        Assert.Equal("https://93.184.216.34/path", result.NormalizedUrl);
    }
}
