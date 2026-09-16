using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace ReadingTracker.Gateway;

/// <summary>
/// How fast any one caller may go. Nothing behind the Gateway can tell a reader from a loop:
/// a signed-in reader — anyone with a Google account — could search until the Books quota was
/// gone for everyone, or add Books by hand until the shared Catalog was full of them. Each pace
/// is set for a person and would take a script to reach (ADR-0013).
/// </summary>
public sealed class RateLimits
{
    public const string SectionName = "RateLimits";

    /// <summary>Each search reaches out to two providers and spends shared quota.</summary>
    public int SearchesPerMinute { get; set; } = 10;

    /// <summary>Each Book added by hand is a row in the shared Catalog, for everyone, for good.</summary>
    public int BooksByHandPerMinute { get; set; } = 5;

    /// <summary>Everything else a reader does, together — a ceiling nobody reaches by hand.</summary>
    public int RequestsPerMinute { get; set; } = 300;

    /// <summary>
    /// Callers with no verified reader, counted by address. They only ever get a 401, so this
    /// bounds how much token checking a stream of junk can cost, and nothing a reader does.
    /// </summary>
    public int AnonymousRequestsPerMinute { get; set; } = 60;
}

public static class ReaderPace
{
    /// <summary>Named in the route configuration, so the routes say which pace applies to them.</summary>
    public const string SearchPolicy = "search";

    public const string ByHandPolicy = "by-hand";

    private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    public static IServiceCollection AddReaderPace(this IServiceCollection services, RateLimits limits) =>
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            // Told when to come back rather than left to guess. The limiter knows to the second.
            options.OnRejected = (rejection, _) =>
            {
                var wait = rejection.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter)
                    ? retryAfter
                    : Window;

                rejection.HttpContext.Response.Headers.RetryAfter =
                    Math.Ceiling(wait.TotalSeconds).ToString(CultureInfo.InvariantCulture);

                return ValueTask.CompletedTask;
            };

            // Every request, by whoever is making it. A reader is counted as themselves, whatever
            // address they come from; a caller with no reader is counted by address, so a stream
            // of junk tokens from one place never draws on any reader's allowance.
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(http =>
                ReaderOf(http) is { } reader
                    ? Partition("reader:" + reader, limits.RequestsPerMinute)
                    : Partition("address:" + AddressOf(http), limits.AnonymousRequestsPerMinute));

            options.AddPolicy(SearchPolicy, http => PerReader(http, limits.SearchesPerMinute));
            options.AddPolicy(ByHandPolicy, http => PerReader(http, limits.BooksByHandPerMinute));
        });

    /// <summary>
    /// A pace for one kind of request, per reader. A caller with no reader is not counted here:
    /// they are about to be turned away with a 401, and the global limiter has already counted
    /// them by address.
    /// </summary>
    private static RateLimitPartition<string> PerReader(HttpContext http, int perMinute) =>
        ReaderOf(http) is { } reader
            ? Partition("reader:" + reader, perMinute)
            : RateLimitPartition.GetNoLimiter("anonymous");

    private static RateLimitPartition<string> Partition(string key, int perMinute) =>
        RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = perMinute,
            Window = Window,
            // Refused now rather than held: a reader is better told to wait than left waiting.
            QueueLimit = 0,
        });

    private static string? ReaderOf(HttpContext http) =>
        http.User.FindFirst(GoogleIdentity.SubjectClaim)?.Value;

    private static string AddressOf(HttpContext http) =>
        http.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}
