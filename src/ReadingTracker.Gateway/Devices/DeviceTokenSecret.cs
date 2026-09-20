using System.Security.Cryptography;
using Microsoft.AspNetCore.WebUtilities;

namespace ReadingTracker.Gateway.Devices;

/// <summary>
/// The secret half of a DeviceToken: how one is made, how it is recognised on the wire, and how
/// it is reduced to something safe to store.
/// </summary>
public static class DeviceTokenSecret
{
    /// <summary>
    /// What every secret starts with. It lets the Gateway route a bearer token to the DeviceToken
    /// check without first trying, and failing, to parse it as a JWT — and it makes a leaked one
    /// recognisable for what it is.
    /// </summary>
    public const string Prefix = "rt_";

    /// <summary>
    /// 256 bits, which is what makes guessing one hopeless. That, not the hash, is the security
    /// of the scheme: the hash only protects against the table itself leaking.
    /// </summary>
    private const int RandomBytes = 32;

    public static string Generate() =>
        Prefix + WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(RandomBytes));

    public static bool IsShapedLike(string? token) =>
        token is not null && token.StartsWith(Prefix, StringComparison.Ordinal);

    /// <summary>
    /// A plain SHA-256, not a password hash. A password hash is slow on purpose because
    /// passwords are guessable; a 256-bit random secret is not, so the slowness would buy
    /// nothing and cost every device request.
    /// </summary>
    public static string Hash(string secret) =>
        Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(secret)));
}
