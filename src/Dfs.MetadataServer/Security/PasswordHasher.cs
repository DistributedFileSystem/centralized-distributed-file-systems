using System.Globalization;
using System.Security.Cryptography;

namespace Dfs.MetadataServer.Security;

/// <summary>Hashes and verifies user passwords using PBKDF2-HMAC-SHA256.</summary>
public static class PasswordHasher
{
    private const string Algorithm = "pbkdf2-sha256";
    private const int Iterations = 600_000;
    private const int SaltLength = 32;
    private const int HashLength = 32;

    /// <summary>Creates a new random salt and a versioned password hash for storage.</summary>
    public static (string PasswordHash, string Salt) HashPassword(string password)
    {
        ArgumentNullException.ThrowIfNull(password);

        var salt = RandomNumberGenerator.GetBytes(SaltLength);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, HashLength);
        return ($"{Algorithm}${Iterations.ToString(CultureInfo.InvariantCulture)}${Convert.ToBase64String(hash)}",
            Convert.ToBase64String(salt));
    }

    /// <summary>Returns false when the password is wrong or the stored hash or salt is malformed.</summary>
    public static bool VerifyPassword(string password, string passwordHash, string salt)
    {
        ArgumentNullException.ThrowIfNull(password);
        if (string.IsNullOrEmpty(passwordHash) || string.IsNullOrEmpty(salt)) return false;

        var parts = passwordHash.Split('$');
        if (parts.Length != 3 || parts[0] != Algorithm ||
            !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var iterations) ||
            iterations < 1 || iterations > 1_000_000)
            return false;

        try
        {
            var saltBytes = Convert.FromBase64String(salt);
            var expected = Convert.FromBase64String(parts[2]);
            if (saltBytes.Length != SaltLength || expected.Length != HashLength) return false;

            var actual = Rfc2898DeriveBytes.Pbkdf2(password, saltBytes, iterations, HashAlgorithmName.SHA256, HashLength);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
