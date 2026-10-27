using System.Security.Cryptography;

namespace Coclico.Security.Infrastructure;

public interface IJournalKeyStore
{
    byte[]? LoadOrCreateKey(string keyPath);
}

public sealed class DpapiJournalKeyStore : IJournalKeyStore
{
    public byte[]? LoadOrCreateKey(string keyPath)
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        try
        {
            if (File.Exists(keyPath))
            {
                byte[] sealedKey = File.ReadAllBytes(keyPath);
                return ProtectedData.Unprotect(sealedKey, null, DataProtectionScope.CurrentUser);
            }

            byte[] key = RandomNumberGenerator.GetBytes(32);
            byte[] protectedKey = ProtectedData.Protect(key, null, DataProtectionScope.CurrentUser);
            string? directory = Path.GetDirectoryName(keyPath);
            if (!string.IsNullOrEmpty(directory))
            {
                _ = Directory.CreateDirectory(directory);
            }

            File.WriteAllBytes(keyPath, protectedKey);
            return key;
        }
        catch (Exception ex) when (ex is IOException or CryptographicException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            return null;
        }
    }
}

public sealed class InMemoryJournalKeyStore : IJournalKeyStore
{
    private readonly byte[] _key;

    public InMemoryJournalKeyStore()
    {
        _key = RandomNumberGenerator.GetBytes(32);
    }

    public byte[]? LoadOrCreateKey(string keyPath)
    {
        return (byte[])_key.Clone();
    }
}
