using System.Text.Json;

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
                body.Note,
                body.Percent,
                body.Chapter,
                body.SavedAt,
                cancellationToken);

            // Created or found already there is the status, 201 or 200: what the device tells the
            // Reader ("Saved" or "Updated").
            return problem is { } refused ? Refusal(refused)
                : saved!.Outcome is QuoteSaveOutcome.Created
                    ? Results.Created($"/api/library/{entryId}/quotes/{saved.Quote.Id}", QuoteResponse.From(saved.Quote))
                    : Results.Ok(QuoteResponse.From(saved.Quote));
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

        // One Quote: where a save's Location points.
        endpoints.MapGet("/api/library/{entryId:guid}/quotes/{quoteId:guid}", async (
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

            var found = await library.FindQuoteAsync(readerId, entryId, quoteId, cancellationToken);

            return found is null ? Results.NotFound() : Results.Ok(QuoteResponse.From(found));
        })
        .WithName("GetQuote");

        // The Reader's note, and only that: a PATCH because it changes one part of the Quote and
        // leaves the rest, which no request can reach. Read as it came rather than bound, since a
        // note left out and a note sent as null bind alike and mean different things: null (or
        // blank) takes the note away, and a change that says nothing about it leaves it be.
        endpoints.MapPatch("/api/library/{entryId:guid}/quotes/{quoteId:guid}", async (
            Guid entryId,
            Guid quoteId,
            JsonElement body,
            HttpRequest request,
            ReadingLibrary library,
            CancellationToken cancellationToken) =>
        {
            if (Reader.From(request) is not { } readerId)
            {
                return LibraryProblems.NotSaidWhoIsAsking();
            }

            if (body.ValueKind is not JsonValueKind.Object)
            {
                return QuoteRejected("note", "Send the change as an object, with the new note as \"note\".");
            }

            NoteChange? change = null;

            if (body.TryGetProperty("note", out var note))
            {
                if (note.ValueKind is not (JsonValueKind.String or JsonValueKind.Null))
                {
                    return QuoteRejected("note", "A note is words, or null to take it away.");
                }

                change = new NoteChange(note.GetString());
            }

            var (quote, problem) = await library.ChangeQuoteNoteAsync(readerId, entryId, quoteId, change, cancellationToken);

            return problem is { } refused ? Refusal(refused) : Results.Ok(QuoteResponse.From(quote!));
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

    private static IResult Refusal(QuoteProblem problem) => problem switch
    {
        QuoteProblem.NotAHighlightId => QuoteRejected(
            "highlightId",
            "A highlight's identity is up to 200 letters, digits, '.', '_', '~' or '-', with at least one letter or digit. Hash or encode what it is made from."),
        QuoteProblem.NoPassage => QuoteRejected(
            "passage",
            "Say which words to keep. A Quote is a passage of the book."),
        QuoteProblem.PassageTooLong => QuoteRejected(
            "passage",
            $"A passage is at most {ReadingLibrary.LongestPassage} characters."),
        QuoteProblem.NotAPercentage => QuoteRejected(
            "percent",
            "Say where in the book the passage is, between 0 and 100 percent."),
        QuoteProblem.NoteTooLong => QuoteRejected(
            "note",
            $"A note is at most {ReadingLibrary.LongestNote} characters."),
        QuoteProblem.ChapterTooLong => QuoteRejected(
            "chapter",
            $"A chapter's name is at most {ReadingLibrary.LongestChapter} characters."),

        // NoSuchEntry and NoSuchQuote: not there, or not this reader's to know about.
        _ => Results.NotFound(),
    };

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
