using ReadingTracker.Gateway.Devices;

namespace ReadingTracker.Gateway.Covers;

/// <summary>
/// Fetches a book's cover on the browser's behalf, so the book page can read its colours.
///
/// The page tints itself from the jacket — the mesh behind it, the accent on it — and to do that
/// the browser has to draw the cover on a canvas and look at the pixels. Google Books serves
/// covers without CORS headers, and a canvas that has drawn a cross-origin image without them
/// is tainted: it will show the picture and refuse to say what colours are in it. Fetched from
/// here, on this origin, the same bytes can be read.
///
/// Only the hosts covers come from, only images, only so large, and only for a reader signed in
/// in person: this is not a general proxy and must not become one. Cached by the browser for a
/// day so a page revisited does not fetch the jacket again.
/// </summary>
public static class CoverEndpoints
{
    public const string ClientName = "covers";

    private static readonly HashSet<string> Hosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "books.google.com",
        "covers.openlibrary.org",
    };

    /// <summary>A cover is a thumbnail. Anything past this is not one, whatever it claims to be.</summary>
    private const int MaxBytes = 2_000_000;

    public static IServiceCollection AddCoverFetching(this IServiceCollection services)
    {
        services.AddHttpClient(ClientName, client =>
        {
            client.Timeout = TimeSpan.FromSeconds(10);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("ReadingTracker/1.0");
        });

        return services;
    }

    public static IEndpointConventionBuilder MapCoverEndpoints(this IEndpointRouteBuilder endpoints) =>
        endpoints.MapGet("/api/covers", async (
            string url,
            IHttpClientFactory clients,
            HttpContext http,
            CancellationToken cancellationToken) =>
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var cover)
                || cover.Scheme is not ("https" or "http")
                || !Hosts.Contains(cover.Host))
            {
                return Results.BadRequest("Not a cover host.");
            }

            HttpResponseMessage response;

            try
            {
                response = await clients.CreateClient(ClientName)
                    .GetAsync(cover, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
            {
                return Results.StatusCode(StatusCodes.Status502BadGateway);
            }

            using (response)
            {
                var mediaType = response.Content.Headers.ContentType?.MediaType;

                if (!response.IsSuccessStatusCode
                    || mediaType is null
                    || !mediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
                    || response.Content.Headers.ContentLength > MaxBytes)
                {
                    return Results.StatusCode(StatusCodes.Status502BadGateway);
                }

                // Read up to the cap and one byte more, so a cover that sent no length and is
                // larger than any cover is caught rather than streamed.
                var buffer = new byte[MaxBytes + 1];
                var read = 0;

                await using (var stream = await response.Content.ReadAsStreamAsync(cancellationToken))
                {
                    int chunk;

                    while (read < buffer.Length
                        && (chunk = await stream.ReadAsync(buffer.AsMemory(read), cancellationToken)) > 0)
                    {
                        read += chunk;
                    }
                }

                if (read > MaxBytes)
                {
                    return Results.StatusCode(StatusCodes.Status502BadGateway);
                }

                http.Response.Headers.CacheControl = "private, max-age=86400";

                return Results.Bytes(buffer.AsMemory(0, read), mediaType);
            }
        })
        .WithName("GetCover")
        .RequireAuthorization(DeviceEndpoints.InPersonPolicy);
}
