using System.Net;
using System.Net.Http.Json;

namespace ReadingTracker.Web.Services;

/// <summary>A LibraryEntry as the Gateway (by way of Library) describes it.</summary>
public sealed record LibraryEntry(
    Guid Id,
    Guid BookId,
    string Status,
    string TrackingMethod,
    BookDetails? Book,
    Progress? Progress);

public sealed record BookDetails(string Title, IReadOnlyList<string> Authors, string? CoverUrl, int? TotalPages);

public sealed record Progress(decimal Position, string Unit, int? PercentComplete);

/// <summary>
/// Why a reader's shelf could not be shown. The three causes are told apart because they call
/// for different things from the reader: signing in again, waiting, or nothing at all.
/// </summary>
public enum LibraryUnavailable
{
    /// <summary>There is no session, or it has ended. The Gateway said 401.</summary>
    NotSignedIn,

    /// <summary>The Gateway itself could not be reached — a network failure, not a refusal.</summary>
    GatewayUnreachable,
}

/// <summary>
/// The browser's window onto a reader's Library, reached through the Gateway rather than
/// Library directly — nothing in this application is allowed to know Library's address.
/// </summary>
public sealed class GatewayLibraryClient(HttpClient httpClient)
{
    public async Task<(IReadOnlyList<LibraryEntry>? Entries, LibraryUnavailable? Problem)> GetEntriesAsync(
        CancellationToken cancellationToken)
    {
        HttpResponseMessage response;

        try
        {
            response = await httpClient.GetAsync("api/library", cancellationToken);
        }
        catch (HttpRequestException)
        {
            // Distinguishable from an empty shelf: nobody answered at all, as opposed to
            // somebody answering "you have no books" or "you are not signed in".
            return (null, LibraryUnavailable.GatewayUnreachable);
        }

        if (response.StatusCode is HttpStatusCode.Unauthorized)
        {
            return (null, LibraryUnavailable.NotSignedIn);
        }

        response.EnsureSuccessStatusCode();

        var entries = await response.Content.ReadFromJsonAsync<IReadOnlyList<LibraryEntry>>(cancellationToken);
        return (entries ?? [], null);
    }
}

public sealed class GatewayOptions
{
    public const string SectionName = "Gateway";

    public Uri BaseAddress { get; set; } = new("http://localhost:5100/");
}
