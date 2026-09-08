using System.Net;
using System.Security.Cryptography;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace ReadingTracker.Gateway.Tests;

/// <summary>
/// Google, as far as the Gateway can tell. Serves the discovery document and signing keys over
/// the stubbed network, and mints tokens signed with a key it actually holds.
///
/// Nothing here reaches inside the Gateway: it validates these tokens with the same code and the
/// same key-fetching machinery it will use against the real Google. That is the point — a test
/// that replaced the validator would prove nothing about whether tokens are really checked.
/// </summary>
public sealed class FakeGoogle
{
    public const string Issuer = "https://accounts.google.com";

    public const string ClientId = "623181146792-testclient.apps.googleusercontent.com";

    private const string DiscoveryPath = "/.well-known/openid-configuration";

    private const string KeysPath = "/oauth2/v3/certs";

    private readonly RsaSecurityKey _signingKey = NewKey();

    /// <summary>How many times the signing keys have been fetched, so a test can prove caching.</summary>
    public int KeyFetches { get; private set; }

    /// <summary>A key that looks like Google's but is not the one Google published.</summary>
    public static RsaSecurityKey UntrustedKey() => NewKey();

    public string Token(
        string subject = "google-subject-1",
        string? audience = null,
        string? issuer = null,
        DateTime? expires = null,
        RsaSecurityKey? signedWith = null) =>
        new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = issuer ?? Issuer,
            Audience = audience ?? ClientId,
            Expires = expires ?? DateTime.UtcNow.AddHours(1),
            Claims = new Dictionary<string, object> { ["sub"] = subject },
            SigningCredentials = new SigningCredentials(
                signedWith ?? _signingKey,
                SecurityAlgorithms.RsaSha256),
        });

    /// <summary>A token with no subject claim, so there is no reader to forward.</summary>
    public string TokenWithoutSubject() =>
        new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = ClientId,
            Expires = DateTime.UtcNow.AddHours(1),
            SigningCredentials = new SigningCredentials(_signingKey, SecurityAlgorithms.RsaSha256),
        });

    /// <summary>Answers the two requests the Gateway makes of Google, and nothing else.</summary>
    public HttpResponseMessage Answer(HttpRequestMessage request)
    {
        var path = request.RequestUri!.AbsolutePath;

        if (path.EndsWith(DiscoveryPath, StringComparison.Ordinal))
        {
            return StubHttpMessageHandler.Json($$"""
                {
                  "issuer": "{{Issuer}}",
                  "jwks_uri": "{{Issuer}}{{KeysPath}}",
                  "authorization_endpoint": "{{Issuer}}/o/oauth2/v2/auth",
                  "token_endpoint": "https://oauth2.googleapis.com/token",
                  "response_types_supported": ["code"],
                  "subject_types_supported": ["public"],
                  "id_token_signing_alg_values_supported": ["RS256"]
                }
                """);
        }

        if (path.EndsWith(KeysPath, StringComparison.Ordinal))
        {
            KeyFetches++;

            var key = JsonWebKeyConverter.ConvertFromRSASecurityKey(_signingKey);
            key.Use = "sig";
            key.Alg = SecurityAlgorithms.RsaSha256;

            return StubHttpMessageHandler.Json(
                $$"""{ "keys": [{{System.Text.Json.JsonSerializer.Serialize(key)}}] }""");
        }

        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    private static RsaSecurityKey NewKey() =>
        new(RSA.Create(2048)) { KeyId = Guid.NewGuid().ToString("N") };
}
