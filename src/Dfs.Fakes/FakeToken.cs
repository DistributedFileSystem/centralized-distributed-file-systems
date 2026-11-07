using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dfs.Shared.Messages;

namespace Dfs.Fakes;

internal static class FakeToken
{
    // This key only protects local fake traffic; it is not a deployment secret.
    private static readonly byte[] Key = Encoding.UTF8.GetBytes("dfs-fakes-local-development-key");

    internal static string Create(string chunkId, string operation)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(new
        {
            u = FakeSamples.UserId,
            c = chunkId,
            op = operation,
            exp = DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeSeconds(),
        });
        return $"{Base64Url(payload)}.{Base64Url(HMACSHA256.HashData(Key, payload))}";
    }

    internal static void Verify(string token, string chunkId, string operation)
    {
        var parts = token.Split('.');
        if (parts.Length != 2) throw Invalid();

        try
        {
            var payload = Decode(parts[0]);
            var signature = Decode(parts[1]);
            var expected = HMACSHA256.HashData(Key, payload);
            if (signature.Length != expected.Length || !CryptographicOperations.FixedTimeEquals(signature, expected))
                throw Invalid();

            using var json = JsonDocument.Parse(payload);
            var root = json.RootElement;
            if (root.GetProperty("c").GetString() != chunkId || root.GetProperty("op").GetString() != operation)
                throw Invalid();
            if (DateTimeOffset.UtcNow.ToUnixTimeSeconds() > root.GetProperty("exp").GetInt64() + 120)
                throw new DfsException(ErrorCode.TokenExpired, "Fake data token expired.");
        }
        catch (Exception error) when (error is FormatException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            throw Invalid();
        }
    }

    private static DfsException Invalid() => new(ErrorCode.TokenInvalid, "Invalid fake data token.");

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Decode(string text)
    {
        var base64 = text.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(base64.PadRight((base64.Length + 3) / 4 * 4, '='));
    }
}
