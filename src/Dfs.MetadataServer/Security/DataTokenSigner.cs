using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Dfs.MetadataServer.Security;

/// <summary>Issues short-lived HMAC tokens for one chunk and one read or write operation.</summary>
public sealed class DataTokenSigner
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);
    private readonly byte[] _key;
    private readonly TimeProvider _time;

    /// <summary>Creates a signer from the Metadata Server's TOKEN_KEY.</summary>
    public DataTokenSigner(string tokenKey, TimeProvider? timeProvider = null)
    {
        if (string.IsNullOrWhiteSpace(tokenKey))
            throw new ArgumentException("TOKEN_KEY must not be empty.", nameof(tokenKey));
        _key = Encoding.UTF8.GetBytes(tokenKey);
        _time = timeProvider ?? TimeProvider.System;
    }

    /// <summary>Signs an authorized user's access to one chunk; operation is r or w.</summary>
    public string Sign(string userId, string chunkId, string operation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentException.ThrowIfNullOrWhiteSpace(chunkId);
        if (operation is not ("r" or "w"))
            throw new ArgumentException("Operation must be r or w.", nameof(operation));

        var expiresAt = _time.GetUtcNow().Add(Lifetime).ToUnixTimeSeconds();
        var payload = JsonSerializer.SerializeToUtf8Bytes(new { u = userId, c = chunkId, op = operation, exp = expiresAt });
        var signature = HMACSHA256.HashData(_key, payload);
        return $"{Base64Url(payload)}.{Base64Url(signature)}";
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
