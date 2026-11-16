using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dfs.Shared.Messages;

namespace Dfs.Shared.Security;

/// <summary>Verifies a chunk access token using the shared HMAC key.</summary>
public sealed class DataTokenVerifier
{
    private const long ClockSkewSeconds = 120;
    private readonly byte[] _key;
    private readonly TimeProvider _time;

    /// <summary>Creates a verifier with the same TOKEN_KEY used by the Metadata Server.</summary>
    public DataTokenVerifier(string tokenKey, TimeProvider? timeProvider = null)
    {
        if (string.IsNullOrWhiteSpace(tokenKey))
            throw new ArgumentException("TOKEN_KEY must not be empty.", nameof(tokenKey));
        _key = Encoding.UTF8.GetBytes(tokenKey);
        _time = timeProvider ?? TimeProvider.System;
    }

    /// <summary>Throws TokenInvalid for a malformed or mismatched token, or TokenExpired after the allowed clock skew.</summary>
    public void Verify(string token, string chunkId, string operation)
    {
        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(chunkId) ||
            operation is not ("r" or "w"))
            throw Invalid();

        var parts = token.Split('.');
        if (parts.Length != 2) throw Invalid();

        try
        {
            var payload = DecodeBase64Url(parts[0]);
            var signature = DecodeBase64Url(parts[1]);
            var expected = HMACSHA256.HashData(_key, payload);
            if (signature.Length != expected.Length ||
                !CryptographicOperations.FixedTimeEquals(signature, expected))
                throw Invalid();

            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("u", out var user) || user.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(user.GetString()) ||
                !root.TryGetProperty("c", out var chunk) || chunk.ValueKind != JsonValueKind.String ||
                chunk.GetString() != chunkId ||
                !root.TryGetProperty("op", out var op) || op.ValueKind != JsonValueKind.String ||
                op.GetString() != operation ||
                !root.TryGetProperty("exp", out var expiry) || expiry.ValueKind != JsonValueKind.Number ||
                !expiry.TryGetInt64(out var exp))
                throw Invalid();

            // Reject an out-of-range Unix timestamp before adding the permitted skew.
            _ = DateTimeOffset.FromUnixTimeSeconds(exp);
            if (_time.GetUtcNow().ToUnixTimeSeconds() > exp + ClockSkewSeconds)
                throw new DfsException(ErrorCode.TokenExpired, "Data token expired.");
        }
        catch (Exception error) when (error is FormatException or JsonException or ArgumentOutOfRangeException)
        {
            throw Invalid();
        }
    }

    private static byte[] DecodeBase64Url(string value)
    {
        if (value.Length == 0 || value.Length % 4 == 1 ||
            value.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('-' or '_')))
            throw Invalid();

        var base64 = value.Replace('-', '+').Replace('_', '/');
        var bytes = Convert.FromBase64String(base64.PadRight((base64.Length + 3) / 4 * 4, '='));
        if (Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_') != value)
            throw Invalid();
        return bytes;
    }

    private static DfsException Invalid() => new(ErrorCode.TokenInvalid, "Invalid data token.");
}
