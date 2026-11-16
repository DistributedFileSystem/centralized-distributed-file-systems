using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dfs.MetadataServer.Security;
using Dfs.Shared.Messages;
using Dfs.Shared.Security;

namespace Dfs.Tests.Security;

public sealed class DataTokenTests
{
    private const string Key = "unit-test-only-shared-token-key";
    private readonly TestTimeProvider _time = new(new DateTimeOffset(2026, 11, 16, 10, 0, 0, TimeSpan.Zero));

    [Theory]
    [InlineData("r")]
    [InlineData("w")]
    public void Metadata_signer_and_shared_verifier_agree_on_scope_and_five_minute_expiry(string operation)
    {
        var token = new DataTokenSigner(Key, _time).Sign("u-1", "chunk-1", operation);
        var parts = token.Split('.');
        Assert.Equal(2, parts.Length);
        Assert.DoesNotContain('=', token);

        using var payload = JsonDocument.Parse(Decode(parts[0]));
        Assert.Equal("u-1", payload.RootElement.GetProperty("u").GetString());
        Assert.Equal("chunk-1", payload.RootElement.GetProperty("c").GetString());
        Assert.Equal(operation, payload.RootElement.GetProperty("op").GetString());
        Assert.Equal(_time.GetUtcNow().ToUnixTimeSeconds() + 300,
            payload.RootElement.GetProperty("exp").GetInt64());
        Assert.Equal(32, Decode(parts[1]).Length);

        new DataTokenVerifier(Key, _time).Verify(token, "chunk-1", operation);
    }

    [Fact]
    public void Wrong_key_signature_chunk_or_operation_returns_token_invalid()
    {
        var token = new DataTokenSigner(Key, _time).Sign("u-1", "chunk-1", "w");
        AssertInvalid(() => new DataTokenVerifier("different-key", _time).Verify(token, "chunk-1", "w"));

        var verifier = new DataTokenVerifier(Key, _time);
        AssertInvalid(() => verifier.Verify(token, "chunk-2", "w"));
        AssertInvalid(() => verifier.Verify(token, "chunk-1", "r"));

        var parts = token.Split('.');
        var expiry = _time.GetUtcNow().ToUnixTimeSeconds() + 300;
        var changedPayload = Base64Url(Encoding.UTF8.GetBytes(
            $"{{\"u\":\"u-2\",\"c\":\"chunk-1\",\"op\":\"w\",\"exp\":{expiry}}}"));
        AssertInvalid(() => verifier.Verify($"{changedPayload}.{parts[1]}", "chunk-1", "w"));
        var changedSignature = Base64Url(new byte[32]);
        AssertInvalid(() => verifier.Verify($"{parts[0]}.{changedSignature}", "chunk-1", "w"));
    }

    [Fact]
    public void Expiry_accepts_exactly_two_minutes_of_clock_skew_then_returns_token_expired()
    {
        var token = new DataTokenSigner(Key, _time).Sign("u-1", "chunk-1", "r");
        var verifier = new DataTokenVerifier(Key, _time);

        _time.Advance(TimeSpan.FromMinutes(7));
        verifier.Verify(token, "chunk-1", "r");

        _time.Advance(TimeSpan.FromSeconds(1));
        var expired = Assert.Throws<DfsException>(() => verifier.Verify(token, "chunk-1", "r"));
        Assert.Equal(ErrorCode.TokenExpired, expired.Code);
    }

    [Fact]
    public void Malformed_tokens_and_signed_payloads_return_token_invalid()
    {
        var verifier = new DataTokenVerifier(Key, _time);
        var expiry = _time.GetUtcNow().ToUnixTimeSeconds() + 300;
        foreach (var malformed in new[] { "", "one-part", "a.b.c", "%.AA", "AA==.AA", "A.AA" })
            AssertInvalid(() => verifier.Verify(malformed, "chunk-1", "r"));

        AssertInvalid(() => verifier.Verify(SignRaw("not json"), "chunk-1", "r"));
        AssertInvalid(() => verifier.Verify(SignRaw($"{{\"c\":\"chunk-1\",\"op\":\"r\",\"exp\":{expiry}}}"), "chunk-1", "r"));
        AssertInvalid(() => verifier.Verify(SignRaw("{\"u\":\"u-1\",\"c\":\"chunk-1\",\"op\":\"r\",\"exp\":\"soon\"}"), "chunk-1", "r"));
    }

    [Fact]
    public void Verifier_checks_the_signed_original_json_bytes()
    {
        var expiry = _time.GetUtcNow().ToUnixTimeSeconds() + 300;
        var token = SignRaw($"{{ \"exp\": {expiry}, \"op\": \"r\", \"c\": \"chunk-1\", \"u\": \"u-1\" }}");

        new DataTokenVerifier(Key, _time).Verify(token, "chunk-1", "r");
    }

    [Fact]
    public void Signer_rejects_missing_key_and_invalid_scope()
    {
        Assert.Throws<ArgumentException>(() => new DataTokenSigner(""));
        Assert.Throws<ArgumentException>(() => new DataTokenVerifier(""));
        var signer = new DataTokenSigner(Key, _time);
        Assert.Throws<ArgumentException>(() => signer.Sign("", "chunk-1", "r"));
        Assert.Throws<ArgumentException>(() => signer.Sign("u-1", "", "r"));
        Assert.Throws<ArgumentException>(() => signer.Sign("u-1", "chunk-1", "delete"));
    }

    private static void AssertInvalid(Action verify) =>
        Assert.Equal(ErrorCode.TokenInvalid, Assert.Throws<DfsException>(verify).Code);

    private static string SignRaw(string json)
    {
        var payload = Encoding.UTF8.GetBytes(json);
        var signature = HMACSHA256.HashData(Encoding.UTF8.GetBytes(Key), payload);
        return $"{Base64Url(payload)}.{Base64Url(signature)}";
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Decode(string text) =>
        Convert.FromBase64String(text.Replace('-', '+').Replace('_', '/').PadRight((text.Length + 3) / 4 * 4, '='));

    private sealed class TestTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan duration) => _now += duration;
    }
}
