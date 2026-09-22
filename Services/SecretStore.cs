using System.Security.Cryptography;
using System.Text;

namespace PlaywrightAgentAI.Services;

/// <summary>
/// Encrypts secrets with the Windows Data Protection API, scoped to the current user.
///
/// The API key must never land in a file that could be committed. DPAPI ties the
/// ciphertext to the Windows user account, so the settings file is useless if copied to
/// another machine or another user's profile - which is the point.
/// </summary>
public static class SecretStore
{
    // Extra entropy so a blob from this app cannot be decrypted by another app running
    // as the same user simply by calling Unprotect on it.
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("PlaywrightAgentAI::ApiKey::v1");

    public static string? Protect(string? plaintext)
    {
        if (string.IsNullOrEmpty(plaintext))
            return null;

        try
        {
            var cipher = ProtectedData.Protect(
                Encoding.UTF8.GetBytes(plaintext),
                Entropy,
                DataProtectionScope.CurrentUser);

            return Convert.ToBase64String(cipher);
        }
        catch (CryptographicException ex)
        {
            Console.Error.WriteLine($"Could not encrypt the API key: {ex.Message}");
            return null;
        }
    }

    public static string? Unprotect(string? encoded)
    {
        if (string.IsNullOrEmpty(encoded))
            return null;

        try
        {
            var plain = ProtectedData.Unprotect(
                Convert.FromBase64String(encoded),
                Entropy,
                DataProtectionScope.CurrentUser);

            return Encoding.UTF8.GetString(plain);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            // Written by a different Windows user, or the file was hand-edited. Treat it
            // as "no key configured" rather than crashing at startup.
            Console.Error.WriteLine("Stored API key could not be decrypted; re-enter it in Settings.");
            return null;
        }
    }
}
