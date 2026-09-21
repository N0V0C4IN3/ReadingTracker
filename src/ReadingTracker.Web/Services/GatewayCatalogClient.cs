using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace ReadingTracker.Web.Services;

/// <summary>A Book as Catalog (by way of the Gateway) describes a search match.</summary>
public sealed record BookSearchResult(
    Guid Id,
    string Title,
    IReadOnlyList<string> Authors,
    string? CoverUrl,
    int? TotalPages);

/// <summary>
/// One page of matches. <paramref name="HasMore"/> is Catalog's word for whether there is a page
/// after this one; there is no total, because neither provider reports one worth quoting.
/// </summary>
public sealed record BookSearchPage(IReadOnlyList<BookSearchResult> Results, int Page, bool HasMore);

/// <summary>
/// A Book as Catalog describes it in full: the basics every listing has, and the longer
/// details only this lookup carries. <paramref name="Description"/> is plain text with a blank
/// line between paragraphs; <paramref name="PublishedDate"/> is whatever the provider knew, a
/// year at least; <paramref name="ProviderUrl"/> is the book's page on the provider's own site,
/// and null for a Book entered by hand; <paramref name="DetailsUnavailable"/> means the details
/// are missing only because the provider could not be reached just now.
/// </summary>
public sealed record CatalogBook(
    Guid Id,
    string Title,
    IReadOnlyList<string> Authors,
    string? Isbn,
    string? CoverUrl,
    int? TotalPages,
    string Source,
    string? Description,
    string? Publisher,
    string? PublishedDate,
    IReadOnlyList<string> Categories,
    string? ProviderUrl,
    bool DetailsUnavailable)
{
    /// <summary>The year a jacket flap would give, from however much of the date the provider had.</summary>
    public string? PublishedYear =>
        PublishedDate is { Length: >= 4 } date && date[..4].All(char.IsAsciiDigit) ? date[..4] : null;

    /// <summary>Who put the book out and when — "Del Rey, 2014" — as much of it as the provider knew.</summary>
    public string? Imprint => (Publisher, PublishedYear) switch
    {
        ({ } publisher, { } year) => $"{publisher}, {year}",
        ({ } publisher, null) => publisher,
        (null, { } year) => year,
        _ => null,
    };

    /// <summary>The description's paragraphs, in order; none when there is no description.</summary>
    public IReadOnlyList<string> Paragraphs =>
        Description?.Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [];

    /// <summary>What to call the link out, by where the Book came from.</summary>
    public string? ProviderName => Source switch
    {
        "GoogleBooks" => "Google Books",
        "OpenLibrary" => "Open Library",
        _ => null,
    };
}

/// <summary>Why one Book could not be fetched.</summary>
public enum BookUnavailable
{
    /// <summary>There is no session, or it has ended. The Gateway said 401.</summary>
    NotSignedIn,

    /// <summary>Catalog has no Book by that id.</summary>
    NotFound,

    /// <summary>The Gateway itself could not be reached — a network failure, not a refusal.</summary>
    GatewayUnreachable,
}

/// <summary>A Book as the reader typed it in, for the times no provider has heard of it.</summary>
public sealed record NewBook(
    string Title,
    IReadOnlyList<string> Authors,
    string? Isbn,
    string? CoverUrl,
    int? TotalPages);

/// <summary>
/// Why a catalog search could not be answered. The causes are told apart because they call for
/// different things from the reader: waiting, signing in again, or nothing at all — a real
/// answer of "no matches" is not one of these.
/// </summary>
public enum SearchUnavailable
{
    /// <summary>There is no session, or it has ended. The Gateway said 401.</summary>
    NotSignedIn,

    /// <summary>No book data provider could be reached. This does not mean the book doesn't exist.</summary>
    ProvidersUnavailable,

    /// <summary>The Gateway itself could not be reached — a network failure, not a refusal.</summary>
    GatewayUnreachable,

    /// <summary>
    /// Catalog read the search and would not run it: too long, or a page past its last. The
    /// boxes are capped to the same length, so a reader only sees this by going around them.
    /// </summary>
    Refused,

    /// <summary>The Gateway said 429: this reader has searched enough for the moment.</summary>
    TooManyRequests,
}

/// <summary>
/// Why a hand-entered Book was not created. Separate from <see cref="SearchUnavailable"/>
/// because creating can fail in a way searching cannot: Catalog can read what was typed and
/// refuse it, field by field.
/// </summary>
public enum BookCreationProblem
{
    /// <summary>There is no session, or it has ended. The Gateway said 401.</summary>
    NotSignedIn,

    /// <summary>The Gateway itself could not be reached — a network failure, not a refusal.</summary>
    GatewayUnreachable,

    /// <summary>Catalog read the details and said no. <c>FieldErrors</c> carries what it said.</summary>
    Rejected,

    /// <summary>A Book already exists for this ISBN. There is one Book per ISBN, catalog-wide.</summary>
    IsbnAlreadyInCatalog,

    /// <summary>The Gateway said 429: this reader has added enough by hand for the moment.</summary>
    TooManyRequests,
}

/// <summary>
/// The outcome of adding a Book by hand. Not a bare enum because a refusal names the fields it
/// is about, and telling a reader which box is wrong is the whole point of reporting it.
/// </summary>
public sealed record BookCreation(
    BookSearchResult? Book,
    BookCreationProblem? Problem,
    IReadOnlyDictionary<string, string[]> FieldErrors)
{
    private static readonly IReadOnlyDictionary<string, string[]> NoFieldErrors =
        new Dictionary<string, string[]>();

    public bool Ok => Problem is null;

    public static BookCreation Succeeded(BookSearchResult book) => new(book, null, NoFieldErrors);

    public static BookCreation Failed(
        BookCreationProblem problem,
        IReadOnlyDictionary<string, string[]>? fieldErrors = null) =>
        new(null, problem, fieldErrors ?? NoFieldErrors);

    /// <summary>What Catalog said about one field, if it said anything about it.</summary>
    public IReadOnlyList<string> For(string field) =>
        FieldErrors.TryGetValue(field, out var messages) ? messages : [];
}

/// <summary>
/// The browser's window onto the Catalog, reached through the Gateway rather than Catalog
/// directly — nothing in this application is allowed to know Catalog's address.
/// </summary>
public sealed class GatewayCatalogClient(HttpClient httpClient)
{
    /// <summary>
    /// How long the Gateway last said to wait when it said 429 (ADR-0013), for a caller that
    /// means to try again rather than give up — the import does. Null until it has said so.
    /// </summary>
    public TimeSpan? RetryAfter { get; private set; }

    /// <summary>
    /// One box's worth of words — a title, an author, an ISBN, some of each. Catalog works out
    /// which; this just hands them over.
    /// </summary>
    public async Task<(BookSearchPage? Page, SearchUnavailable? Problem)> SearchAsync(
        string query,
        int page,
        CancellationToken cancellationToken)
    {
        HttpResponseMessage response;

        try
        {
            response = await httpClient.GetAsync(
                $"api/books/search?q={Uri.EscapeDataString(query.Trim())}&page={page}",
                cancellationToken);
        }
        catch (HttpRequestException)
        {
            // Distinguishable from a real "no matches" answer: nobody answered at all.
            return (null, SearchUnavailable.GatewayUnreachable);
        }

        if (response.StatusCode is HttpStatusCode.Unauthorized)
        {
            return (null, SearchUnavailable.NotSignedIn);
        }

        if (response.StatusCode is HttpStatusCode.ServiceUnavailable)
        {
            return (null, SearchUnavailable.ProvidersUnavailable);
        }

        if (response.StatusCode is HttpStatusCode.BadRequest)
        {
            return (null, SearchUnavailable.Refused);
        }

        if (response.StatusCode is HttpStatusCode.TooManyRequests)
        {
            RetryAfter = response.Headers.RetryAfter?.Delta;
            return (null, SearchUnavailable.TooManyRequests);
        }

        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<SearchResponseBody>(cancellationToken);

        return (new BookSearchPage(body?.Results ?? [], body?.Page ?? page, body?.HasMore ?? false), null);
    }

    /// <summary>
    /// One Book in full, for its page. The first time anybody asks for a Book, Catalog may go
    /// to the provider for its longer details, so this can take a moment longer than a search.
    /// </summary>
    public async Task<(CatalogBook? Book, BookUnavailable? Problem)> GetBookAsync(
        Guid bookId,
        CancellationToken cancellationToken)
    {
        HttpResponseMessage response;

        try
        {
            response = await httpClient.GetAsync($"api/books/{bookId}", cancellationToken);
        }
        catch (HttpRequestException)
        {
            return (null, BookUnavailable.GatewayUnreachable);
        }

        if (response.StatusCode is HttpStatusCode.Unauthorized)
        {
            return (null, BookUnavailable.NotSignedIn);
        }

        if (response.StatusCode is HttpStatusCode.NotFound)
        {
            return (null, BookUnavailable.NotFound);
        }

        // The Gateway answering for a Catalog it could not reach — 502, 503 — is the same thing
        // to the page as not reaching the Gateway: the book half has nothing to show, and says so.
        if (!response.IsSuccessStatusCode)
        {
            return (null, BookUnavailable.GatewayUnreachable);
        }

        var book = await response.Content.ReadFromJsonAsync<CatalogBook>(cancellationToken);

        return book is null ? (null, BookUnavailable.NotFound) : (book, null);
    }

    /// <summary>
    /// Records a Book nobody's provider has, and hands back the same shape a search match takes —
    /// the caller's next move is to add it to the reader's library, exactly as for a found book.
    /// </summary>
    public async Task<BookCreation> CreateAsync(NewBook book, CancellationToken cancellationToken)
    {
        HttpResponseMessage response;

        try
        {
            response = await httpClient.PostAsJsonAsync("api/books", book, cancellationToken);
        }
        catch (HttpRequestException)
        {
            return BookCreation.Failed(BookCreationProblem.GatewayUnreachable);
        }

        if (response.StatusCode is HttpStatusCode.Unauthorized)
        {
            return BookCreation.Failed(BookCreationProblem.NotSignedIn);
        }

        if (response.StatusCode is HttpStatusCode.Conflict)
        {
            return BookCreation.Failed(BookCreationProblem.IsbnAlreadyInCatalog);
        }

        if (response.StatusCode is HttpStatusCode.TooManyRequests)
        {
            RetryAfter = response.Headers.RetryAfter?.Delta;
            return BookCreation.Failed(BookCreationProblem.TooManyRequests);
        }

        if (response.StatusCode is HttpStatusCode.BadRequest)
        {
            return BookCreation.Failed(BookCreationProblem.Rejected, await FieldErrorsFrom(response, cancellationToken));
        }

        response.EnsureSuccessStatusCode();

        var created = await response.Content.ReadFromJsonAsync<BookSearchResult>(cancellationToken);

        // A 201 with nothing readable in it is not a Book we can go on to add to a library.
        return created is null
            ? BookCreation.Failed(BookCreationProblem.Rejected)
            : BookCreation.Succeeded(created);
    }

    /// <summary>
    /// Catalog's own words, kept as it grouped them. Its validation messages are already written
    /// for a person to read — "at least one author is required" — so they are repeated rather
    /// than restated less precisely here, and Catalog's rules are not duplicated client-side.
    /// </summary>
    private static async Task<IReadOnlyDictionary<string, string[]>> FieldErrorsFrom(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            var details = await response.Content.ReadFromJsonAsync<ValidationProblem>(cancellationToken);

            // Case-insensitively, because a field is named by whichever service is describing it:
            // Catalog says "Title" where the form knows the same box by a lowercase name.
            return details?.Errors is not { } errors
                ? new Dictionary<string, string[]>()
                : new Dictionary<string, string[]>((IDictionary<string, string[]>)errors, StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            // A refusal we cannot read is still a refusal; the caller says so in general terms.
            return new Dictionary<string, string[]>();
        }
    }

    private sealed record SearchResponseBody(IReadOnlyList<BookSearchResult> Results, int Page, bool HasMore);

    private sealed record ValidationProblem(Dictionary<string, string[]>? Errors);
}
