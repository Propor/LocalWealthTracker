using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace LocalWealthTracker.Services;

/// <summary>
/// Encrypts/decrypts the POESESSID using Windows DPAPI.
/// 
/// - Encrypted with DataProtectionScope.CurrentUser
/// - Only YOUR Windows login on THIS machine can decrypt it
/// - Stored as a separate file, not in settings.json
/// - settings.json no longer contains the session ID at all
/// </summary>
public static class CredentialService
{
    private static readonly string AppDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "LocalWealthTracker");

    private static readonly string CredFile = Path.Combine(AppDir, "session.bin");

    /// <summary>
    /// Encrypts and saves the POESESSID to disk.
    /// </summary>
    public static void Save(string sessionId)
    {
        Directory.CreateDirectory(AppDir);

        var plain = Encoding.UTF8.GetBytes(sessionId);
        var encrypted = ProtectedData.Protect(
            plain,
            optionalEntropy: null,
            scope: DataProtectionScope.CurrentUser);

        File.WriteAllBytes(CredFile, encrypted);
    }

    /// <summary>
    /// Loads and decrypts the POESESSID from disk.
    /// Returns null if no credential is stored or decryption fails.
    /// </summary>
    public static string? Load()
    {
        try
        {
            if (!File.Exists(CredFile)) return null;

            var encrypted = File.ReadAllBytes(CredFile);
            var plain = ProtectedData.Unprotect(
                encrypted,
                optionalEntropy: null,
                scope: DataProtectionScope.CurrentUser);

            return Encoding.UTF8.GetString(plain);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Deletes the stored credential.
    /// </summary>
    public static void Delete()
    {
        try
        {
            if (File.Exists(CredFile))
                File.Delete(CredFile);
        }
        catch { }
    }

    /// <summary>
    /// Returns true if a credential is stored.
    /// </summary>
    public static bool Exists() => File.Exists(CredFile);
}