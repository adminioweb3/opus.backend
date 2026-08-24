namespace Citationly.Infrastructure.Services;

/// <summary>
/// appsettings.json ships literal "${ENV_VAR_NAME}" placeholders for every secret (OpenAI,
/// Stripe, and now the other AI providers) so the expected config shape is visible in source
/// without a real secret ever being committed. Treat an unresolved placeholder the same as
/// "not set" everywhere a config value's presence gates behavior (IsConfigured checks, etc.).
/// </summary>
internal static class ConfigPlaceholderHelper
{
    public static string? Resolve(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return value.StartsWith("${", StringComparison.Ordinal) ? null : value;
    }
}
