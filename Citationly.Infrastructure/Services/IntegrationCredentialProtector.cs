using System.Security.Cryptography;
using System.Text;
using Citationly.Application.Interfaces;
using Microsoft.Extensions.Configuration;

namespace Citationly.Infrastructure.Services;

public sealed class IntegrationCredentialProtector : IIntegrationCredentialProtector
{
    private const string Prefix = "enc:v1:";
    private readonly byte[]? _key;

    public IntegrationCredentialProtector(IConfiguration configuration)
    {
        var configured = ConfigPlaceholderHelper.Resolve(configuration["Integrations:EncryptionKey"])
            ?? ConfigPlaceholderHelper.Resolve(configuration["INTEGRATION_ENCRYPTION_KEY"]);
        if (!string.IsNullOrWhiteSpace(configured) && !configured.StartsWith("${", StringComparison.Ordinal))
        {
            try
            {
                var decoded = Convert.FromBase64String(configured);
                if (decoded.Length == 32) _key = decoded;
            }
            catch (FormatException)
            {
                // Protect() returns an actionable configuration error without exposing the value.
            }
        }
    }

    public string Protect(string plaintext)
    {
        if (_key == null)
            throw new InvalidOperationException("Integration credential encryption is not configured. Set Integrations__EncryptionKey to a base64-encoded 32-byte key.");

        var nonce = RandomNumberGenerator.GetBytes(12);
        var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        var ciphertext = new byte[plaintextBytes.Length];
        var tag = new byte[16];
        using var aes = new AesGcm(_key, tag.Length);
        aes.Encrypt(nonce, plaintextBytes, ciphertext, tag);

        var payload = new byte[nonce.Length + tag.Length + ciphertext.Length];
        Buffer.BlockCopy(nonce, 0, payload, 0, nonce.Length);
        Buffer.BlockCopy(tag, 0, payload, nonce.Length, tag.Length);
        Buffer.BlockCopy(ciphertext, 0, payload, nonce.Length + tag.Length, ciphertext.Length);
        CryptographicOperations.ZeroMemory(plaintextBytes);
        return Prefix + Convert.ToBase64String(payload);
    }

    public string Unprotect(string protectedValue)
    {
        if (!protectedValue.StartsWith(Prefix, StringComparison.Ordinal))
            return protectedValue; // Legacy credentials remain usable until the connection is updated.
        if (_key == null)
            throw new InvalidOperationException("Integration credential encryption is not configured.");

        var payload = Convert.FromBase64String(protectedValue[Prefix.Length..]);
        if (payload.Length < 29) throw new CryptographicException("The integration credential is invalid.");
        var nonce = payload.AsSpan(0, 12);
        var tag = payload.AsSpan(12, 16);
        var ciphertext = payload.AsSpan(28);
        var plaintext = new byte[ciphertext.Length];
        using var aes = new AesGcm(_key, tag.Length);
        aes.Decrypt(nonce, ciphertext, tag, plaintext);
        try
        {
            return Encoding.UTF8.GetString(plaintext);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }
}
