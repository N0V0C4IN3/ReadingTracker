namespace ReadingTracker.Library.Entries;

/// <summary>
/// The Quotes on one LibraryEntry: passages a Reader sent on purpose from their device, and kept
/// by the tracker from then on (ADR-0017). A device saves them; the Book page lists them, edits
/// their notes and deletes them. Nothing changes a passage.
/// </summary>
public static class QuoteEndpoints
{
    /// <summary>
    /// The identity is the device's to make up and Library's to look up by. The plugin builds it
    /// from a time and a position, which is a few dozen characters; anything far longer is not an
    /// identity, and would not fit the index that keeps a highlight one Quote.
    /// </summary>
    private const int LongestHighlightId = 200;

    public static void MapQuoteEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // A device saving a highlight. Keyed by the highlight's identity rather than posted, so
        // sending it again — a retry, or the Reader saving a changed note — is the same Quote.
        endpoints.MapPut("/api/library/{entryId:guid}/quotes/{highlightId}", async (
            Guid entryId,
            string highlightId,
            SaveQuoteRequest body,
            HttpRequest request,
            ReadingLibrary library,
            CancellationToken cancellationToken) =>
        {
            if (Reader.From(request) is not { } readerId)
            {
                return LibraryProblems.NotSaidWhoIsAsking();
            }

            if (highlightId.Length > LongestHighlightId)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["highlightId"] = [$"A highlight's identity is at most {LongestHighlightId} characters."],
                });
            }

            if (string.IsNullOrWhiteSpace(body.Passage))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["passage"] = ["Say which words to keep. A Quote is a passage of the book."],
                });
            }

            // Missing is refused too, rather than read as the start of the book.
            if (body.Percent is not { } percent || percent is < 0m or > 100m)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["percent"] = ["Say where in the book the passage is, between 0 and 100 percent."],
                });
            }

            var (quote, outcome, problem) = await library.SaveQuoteAsync(
                readerId,
                entryId,
                highlightId,
                body.Passage,
                NoneIfBlank(body.Note),
                percent,
                NoneIfBlank(body.Chapter),
                body.SavedAt,
                cancellationToken);

            if (problem is not null)
            {
                return Results.NotFound();
            }

            var answer = new SavedQuoteResponse(outcome.ToString(), QuoteResponse.From(quote!));

            return outcome is QuoteSaveOutcome.Created
                ? Results.Created($"/api/library/{entryId}/quotes/{quote!.Id}", answer)
                : Results.Ok(answer);
        })
        .WithName("SaveQuote");

        endpoints.MapGet("/api/library/{entryId:guid}/quotes", async (
            Guid entryId,
            HttpRequest request,
            ReadingLibrary library,
            CancellationToken cancellationToken) =>
        {
            if (Reader.From(request) is not { } readerId)
            {
                return LibraryProblems.NotSaidWhoIsAsking();
            }

            if (await library.FindAsync(readerId, entryId, cancellationToken) is null)
            {
                return Results.NotFound();
            }

            var quotes = await library.ListQuotesAsync(entryId, cancellationToken);

            return Results.Ok(quotes.Select(QuoteResponse.From));
        })
        .WithName("ListQuotes");

        // The Reader's note, and only that: a PATCH because it changes one part of the Quote and
        // leaves the rest, which no request can reach.
        endpoints.MapPatch("/api/library/{entryId:guid}/quotes/{quoteId:guid}", async (
            Guid entryId,
            Guid quoteId,
            NoteQuoteRequest body,
            HttpRequest request,
            ReadingLibrary library,
            CancellationToken cancellationToken) =>
        {
            if (Reader.From(request) is not { } readerId)
            {
                return LibraryProblems.NotSaidWhoIsAsking();
            }

            var quote = await library.NoteQuoteAsync(readerId, entryId, quoteId, NoneIfBlank(body.Note), cancellationToken);

            return quote is null ? Results.NotFound() : Results.Ok(QuoteResponse.From(quote));
        })
        .WithName("NoteQuote");
    }

    /// <summary>A blank note or chapter is none: there is nothing in it to show.</summary>
    private static string? NoneIfBlank(string? text) => string.IsNullOrWhiteSpace(text) ? null : text;

    /// <summary>
    /// A highlight as the device describes it. <paramref name="Percent"/> is where the passage
    /// starts in the Book, measured as the Bookmark is; <paramref name="SavedAt"/> is when the
    /// Reader saved it, by the device's clock.
    /// </summary>
    private sealed record SaveQuoteRequest(
        string? Passage,
        string? Note,
        decimal? Percent,
        string? Chapter,
        DateTimeOffset? SavedAt);

    /// <summary>The Reader's new note. Null or blank takes the note away.</summary>
    private sealed record NoteQuoteRequest(string? Note);

    /// <summary>The Quote as it now stands, and whether this save made it or found it already there.</summary>
    private sealed record SavedQuoteResponse(string Outcome, QuoteResponse Quote);

    private sealed record QuoteResponse(
        Guid Id,
        string HighlightId,
        string Passage,
        string? Note,
        decimal Percent,
        string? Chapter,
        DateTimeOffset SavedAt)
    {
        public static QuoteResponse From(Quote quote) =>
            new(quote.Id, quote.HighlightId, quote.Passage, quote.Note, quote.Percent, quote.Chapter, quote.SavedAt);
    }
}
