using Citationly.Infrastructure.Services.Companies;
using Xunit;

namespace Citationly.Tests;

public class CompanyProfileSummarizerTests
{
    [Fact]
    public void ExtractContext_UsesSubmittedSourceContext_WhenInferredFieldsAreEmpty()
    {
        const string profile = """
            {
              "industriesServed": { "value": [] },
              "coreServices": { "value": [] },
              "products": { "value": [] },
              "targetCustomers": { "value": [] },
              "sourceContext": {
                "industry": "Healthcare",
                "whoDoYouSellTo": "Independent clinics",
                "mainOffering": "Appointment automation",
                "knownCompetitors": "Acme Health, Clinic Tools"
              }
            }
            """;

        var context = CompanyProfileSummarizer.ExtractContext(profile);

        Assert.Equal("Healthcare", context.Industry);
        Assert.Equal("Independent clinics", context.TargetAudience);
        Assert.Equal("Appointment automation", context.Services);
        Assert.Equal("Appointment automation", context.Products);
        Assert.Equal("Acme Health, Clinic Tools", context.KnownCompetitors);
    }
}
