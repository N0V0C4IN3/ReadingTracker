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
    public async Task<(BookSearchPage? Page, SearchUnavailable? Problem)> SearchAsync(
        string? isbn,
        string? title,
        string? author,
        int page,
        CancellationToken cancellationToken)
    {
        HttpResponseMessage response;

        try
        {
            response = await httpClient.GetAsync(
                $"api/books/search{BuildQuery(isbn, title, author, page)}",
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

        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<SearchResponseBody>(cancellationToken);

        return (new BookSearchPage(body?.Results ?? [], body?.Page ?? page, body?.HasMore ?? false), null);
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

    private static string BuildQuery(string? isbn, string? title, string? author, int page)
    {
        var parameters = new List<string> { $"page={page}" };

        if (!string.IsNullOrWhiteSpace(isbn))
        {
            parameters.Add($"isbn={Uri.EscapeDataString(isbn)}");
        }

        if (!string.IsNullOrWhiteSpace(title))
        {
            parameters.Add($"title={Uri.EscapeDataString(title)}");
        }

        if (!string.IsNullOrWhiteSpace(author))
        {
            parameters.Add($"author={Uri.EscapeDataString(author)}");
        }

        return parameters.Count == 0 ? "" : $"?{string.Join("&", parameters)}";
    }

    private sealed record SearchResponseBody(IReadOnlyList<BookSearchResult> Results, int Page, bool HasMore);

    private sealed record ValidationProblem(Dictionary<string, string[]>? Errors);
}
