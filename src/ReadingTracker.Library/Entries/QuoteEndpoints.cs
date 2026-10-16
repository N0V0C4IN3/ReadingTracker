namespace ReadingTracker.Library.Entries;

/// <summary>
/// The Quotes on one LibraryEntry: passages a Reader sent on purpose from their device, and kept
/// by the tracker from then on (ADR-0017). A device saves them; the Book page lists them, edits
/// their notes and deletes them. Nothing changes a passage.
/// </summary>
public static class QuoteEndpoints
{
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

            var (saved, problem) = await library.SaveQuoteAsync(
                readerId,
                entryId,
                highlightId,
                body.Passage,
                NoneIfBlank(body.Note),
                body.Percent,
                NoneIfBlank(body.Chapter),
                body.SavedAt,
                cancellationToken);

            return problem switch
            {
                QuoteProblem.NoSuchEntry => Results.NotFound(),
                QuoteProblem.NotAHighlightId => QuoteRejected(
                    "highlightId",
                    "A highlight's identity is up to 200 letters, digits, '.', '_', '~' or '-'. Hash or encode what it is made from."),
                QuoteProblem.NoPassage => QuoteRejected(
                    "passage",
                    "Say which words to keep. A Quote is a passage of the book."),
                QuoteProblem.NotAPercentage => QuoteRejected(
                    "percent",
                    "Say where in the book the passage is, between 0 and 100 percent."),
                _ => saved!.Outcome is QuoteSaveOutcome.Created
                    ? Results.Created($"/api/library/{entryId}/quotes/{saved.Quote.Id}", SavedQuoteResponse.From(saved))
                    : Results.Ok(SavedQuoteResponse.From(saved)),
            };
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
            ChangeNoteRequest body,
            HttpRequest request,
            ReadingLibrary library,
            CancellationToken cancellationToken) =>
        {
            if (Reader.From(request) is not { } readerId)
            {
                return LibraryProblems.NotSaidWhoIsAsking();
            }

            var quote = await library.ChangeQuoteNoteAsync(readerId, entryId, quoteId, NoneIfBlank(body.Note), cancellationToken);

            return quote is null ? Results.NotFound() : Results.Ok(QuoteResponse.From(quote));
        })
        .WithName("ChangeQuoteNote");

        endpoints.MapDelete("/api/library/{entryId:guid}/quotes/{quoteId:guid}", async (
            Guid entryId,
            Guid quoteId,
            HttpRequest request,
            ReadingLibrary library,
            CancellationToken cancellationToken) =>
        {
            if (Reader.From(request) is not { } readerId)
            {
                return LibraryProblems.NotSaidWhoIsAsking();
            }

            return await library.DeleteQuoteAsync(readerId, entryId, quoteId, cancellationToken)
                ? Results.NoContent()
                : Results.NotFound();
        })
        .WithName("DeleteQuote");
    }

    /// <summary>A blank note or chapter is none: there is nothing in it to show.</summary>
    private static string? NoneIfBlank(string? text) => string.IsNullOrWhiteSpace(text) ? null : text;

    private static IResult QuoteRejected(string field, string detail) =>
        Results.ValidationProblem(new Dictionary<string, string[]> { [field] = [detail] });

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
    private sealed record ChangeNoteRequest(string? Note);

    /// <summary>
    /// The Quote as it now stands, and whether this save made it or found it already there —
    /// "Created" or "Updated", which is what the device tells the Reader.
    /// </summary>
    private sealed record SavedQuoteResponse(string Outcome, QuoteResponse Quote)
    {
        public static SavedQuoteResponse From(SavedQuote saved) =>
            new(saved.Outcome.ToString(), QuoteResponse.From(saved.Quote));
    }

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
