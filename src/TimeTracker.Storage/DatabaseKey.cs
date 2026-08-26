using System.Security.Cryptography;

namespace TimeTracker.Storage;

/// <summary>
/// Manages the database encryption key (brief §13: "sensitive local data should be encrypted
/// and protected from unauthorized access").
/// </summary>
/// <remarks>
/// <para>
/// A 256-bit key is generated on first run and stored on disk wrapped by Windows DPAPI at
/// <see cref="DataProtectionScope.CurrentUser"/> scope. That ties it to the Windows account:
/// another user on the same machine, or the same file copied to another machine, cannot
/// unwrap it.
/// </para>
/// <para>
/// <b>The trade-off, stated plainly.</b> DPAPI means no password to remember and no way to
/// lose the history by forgetting one — which is why it suits a pilot. It also means anyone
/// who can execute code <i>as that Windows user</i> can read the database. A user passphrase
/// would resist that, at the cost of losing all history when someone forgets it. This is the
/// right default for a trust-based internal tool; it is not the right default for a device
/// that leaves the building unlocked.
/// </para>
/// </remarks>
public sealed class DatabaseKey
{
    private const int KeyBytes = 32;

    /// <summary>Additional entropy, so the blob is not unwrappable by unrelated DPAPI callers.</summary>
    private static readonly byte[] Entropy = "TimeTracker.LocalDb.v1"u8.ToArray();

    private readonly string _keyPath;

    public DatabaseKey(string keyPath) => _keyPath = keyPath;

    /// <summary>
    /// Returns the key as hex, generating and persisting one if this is first run.
    /// </summary>
    public string GetOrCreate()
    {
        if (File.Exists(_keyPath))
            return Convert.ToHexString(Unwrap(File.ReadAllBytes(_keyPath)));

        var key = RandomNumberGenerator.GetBytes(KeyBytes);

        Directory.CreateDirectory(Path.GetDirectoryName(_keyPath)!);

        // Written to a temp file then moved, so an interrupted first run cannot leave a
        // truncated key file behind — which would lock the user out of their own database.
        var temp = _keyPath + ".tmp";
        File.WriteAllBytes(temp, Wrap(key));
        File.Move(temp, _keyPath, overwrite: true);

        return Convert.ToHexString(key);
    }

    private static byte[] Wrap(byte[] key)
        => ProtectedData.Protect(key, Entropy, DataProtectionScope.CurrentUser);

    private static byte[] Unwrap(byte[] blob)
    {
        try
        {
            return ProtectedData.Unprotect(blob, Entropy, DataProtectionScope.CurrentUser);
        }
        catch (CryptographicException ex)
        {
            // Happens when the key file is copied from another machine or user profile.
            // Fail loudly: silently regenerating would create a second key and make the
            // existing database permanently unreadable.
            throw new InvalidOperationException(
                "The local database key could not be unwrapped. It belongs to a different " +
                "Windows user or machine. The existing database cannot be opened.", ex);
        }
    }
}
