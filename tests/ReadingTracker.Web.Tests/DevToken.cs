using System.Text;
using System.Text.Json;

namespace ReadingTracker.Web.Tests;

/// <summary>
/// Tokens shaped like the ones the Gateway's dev sign-in mints. Unsigned: nothing on the browser
/// side of the wire verifies a signature — the Gateway does that on every request — so a real one
/// would prove nothing here that this does not.
/// </summary>
public static class DevToken
{
    /// <summary>
    /// <paramref name="nonce"/> only distinguishes one token from another for the same reader,
    /// which is what a test of renewal needs to be able to tell apart.
    /// </summary>
    public static string For(string readerId, string? nonce = null)
    {
        var payload = JsonSerializer.Serialize(new
        {
            sub = readerId,
            name = $"Dev Reader ({readerId})",
            jti = nonce ?? "1",
        });

        return $"{Base64Url("{\"alg\":\"HS256\",\"typ\":\"JWT\"}")}.{Base64Url(payload)}.signature";
    }

    private static string Base64Url(string value) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(value)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
