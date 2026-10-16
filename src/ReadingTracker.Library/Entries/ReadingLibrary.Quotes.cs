using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace ReadingTracker.Library.Entries;

/// <summary>The Quotes on a reader's entries (ADR-0017).</summary>
public sealed partial class ReadingLibrary
{
    /// <summary>
    /// Keeps a passage a device sent, or finds the one it already sent under the same
    /// <paramref name="highlightId"/> and replaces its note with <paramref name="note"/>: the one
    /// way an edit made on the device reaches the tracker, so no note on the device is no note
    /// here. The passage, where it is and when it was first saved stand as first sent: the Book's
    /// words do not change, and a retried request must not be able to move them.
    ///
    /// A save that meets the Reader deleting the same Quote, or taking the book off the shelf, at
    /// the same moment is never a failure: whichever got there first stands, and this save is
    /// what it would have been a moment later — the Quote sent again, or no such entry.
    /// </summary>
    public async Task<(SavedQuote? Saved, QuoteProblem? Problem)> SaveQuoteAsync(
        string readerId,
        Guid entryId,
        string highlightId,
        string? passage,
        string? note,
        decimal? percent,
        string? chapter,
        DateTimeOffset? savedAt,
        CancellationToken cancellationToken)
    {
        if (!HighlightIdentity().IsMatch(highlightId))
        {
            return (null, QuoteProblem.NotAHighlightId);
        }

        passage = Kept(passage);
        note = Kept(note);
        chapter = Kept(chapter);

        if (passage is null)
        {
            return (null, QuoteProblem.NoPassage);
        }

        // Missing is refused too, rather than read as the start of the book.
        if (percent is not { } position || position is < 0m or > 100m)
        {
            return (null, QuoteProblem.NotAPercentage);
        }

        var entry = await FindAsync(readerId, entryId, cancellationToken);

        if (entry is null)
        {
            return (null, QuoteProblem.NoSuchEntry);
        }

        // UTC for the same reason as a session's time: see LogSessionAsync. To the microsecond,
        // which is all the database keeps, so the save answers with the moment it kept.
        var saved = (savedAt ?? clock.GetUtcNow()).ToUniversalTime();
        saved = saved.AddTicks(-(saved.Ticks % TimeSpan.TicksPerMicrosecond));

        // Round again only when another request changed things between looking and writing; the
        // second look sees what it did. Twice is plenty: each round lost is a request that won.
        for (var round = 0; ; round++)
        {
            if (await FindQuoteByHighlightAsync(entry.Id, highlightId, cancellationToken) is { } sent
                && await ReplaceNoteAsync(sent, note, cancellationToken) is { } again)
            {
                return (new SavedQuote(again, QuoteSaveOutcome.Updated), null);
            }

            // Not sent before, or deleted as this changed its note: then the delete came first,
            // and the highlight still on the device makes it a new Quote (ADR-0017).
            var quote = new Quote
            {
                Id = Guid.CreateVersion7(),
                LibraryEntryId = entry.Id,
                HighlightId = highlightId,
                Passage = passage,
                Note = note,
                Percent = position,
                Chapter = chapter,
                SavedAt = saved,
            };

            database.Quotes.Add(quote);

            try
            {
                await database.SaveChangesAsync(cancellationToken);

                return (new SavedQuote(quote, QuoteSaveOutcome.Created), null);
            }
            catch (DbUpdateException exception)
                when (round < 2 && exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                // The same highlight arrived twice at once — a device retrying a request that had
                // not actually failed — and the other got there first; the unique index said so.
                // This one is the resend it would have been a moment later.
                database.Entry(quote).State = EntityState.Detached;
            }
            catch (DbUpdateException exception)
                when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.ForeignKeyViolation })
            {
                // The book left the shelf after it was found: there is no entry to keep it on.
                database.Entry(quote).State = EntityState.Detached;

                return (null, QuoteProblem.NoSuchEntry);
            }
        }
    }

    /// <summary>
    /// An entry's Quotes in book order: by where each is in the Book, and two at the same place by
    /// when each was saved — and two saved at the same moment by which arrived first, so the
    /// order never shuffles between one look and the next. The caller must have found the entry
    /// for this reader.
    /// </summary>
    public Task<List<Quote>> ListQuotesAsync(Guid entryId, CancellationToken cancellationToken) =>
        database.Quotes
            .Where(quote => quote.LibraryEntryId == entryId)
            .OrderBy(quote => quote.Percent)
            .ThenBy(quote => quote.SavedAt)
            .ThenBy(quote => quote.Id)
            .ToListAsync(cancellationToken);

    /// <summary>
    /// How many Quotes each entry has, for the Book page to decide whether to offer them and what
    /// to call the button. An entry with none is left out rather than counted as zero.
    /// </summary>
    public async Task<IReadOnlyDictionary<Guid, int>> QuoteCountsAsync(
        IReadOnlyCollection<Guid> entryIds,
        CancellationToken cancellationToken) =>
        entryIds.Count == 0
            ? new Dictionary<Guid, int>()
            : await database.Quotes
                .Where(quote => entryIds.Contains(quote.LibraryEntryId))
                .GroupBy(quote => quote.LibraryEntryId)
                .Select(perEntry => new { EntryId = perEntry.Key, Count = perEntry.Count() })
                .ToDictionaryAsync(counted => counted.EntryId, counted => counted.Count, cancellationToken);

    /// <summary>
    /// Changes the Reader's note on a Quote, or takes it away with null or blank. The note is the
    /// only thing about a Quote that can change: the passage is the Book's words. Null when there
    /// is no such Quote — including one deleted while this was on its way.
    /// </summary>
    public async Task<Quote?> ChangeQuoteNoteAsync(
        string readerId,
        Guid entryId,
        Guid quoteId,
        string? note,
        CancellationToken cancellationToken) =>
        await FindQuoteAsync(readerId, entryId, quoteId, cancellationToken) is { } quote
            ? await ReplaceNoteAsync(quote, Kept(note), cancellationToken)
            : null;

    /// <summary>
    /// Removes a Quote, at once, as deleting a ReadingSession does. It can come back only if its
    /// highlight is still on the device to send again (ADR-0017). False when there is no such
    /// Quote, including one another request deleted first.
    /// </summary>
    public async Task<bool> DeleteQuoteAsync(
        string readerId,
        Guid entryId,
        Guid quoteId,
        CancellationToken cancellationToken)
    {
        var quote = await FindQuoteAsync(readerId, entryId, quoteId, cancellationToken);

        if (quote is null)
        {
            return false;
        }

        database.Quotes.Remove(quote);

        try
        {
            await database.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            database.Entry(quote).State = EntityState.Detached;

            return false;
        }

        return true;
    }

    /// <summary>
    /// What a highlight's identity may be made of: the characters that mean the same thing
    /// escaped or not, so it reaches Library exactly as the device made it, through the Gateway
    /// and anything else in front of it. A slash is the one that matters — a KOReader position is
    /// full of them, and an escaped one is left escaped by one hop and decoded by another. The
    /// device hashes or encodes what it builds the identity from. At most 200 characters, which is
    /// far more than an identity needs and well inside what the index keeping a highlight one
    /// Quote can hold. \z rather than $, which would let a line break through at the end.
    /// </summary>
    [GeneratedRegex(@"^[A-Za-z0-9._~-]{1,200}\z")]
    private static partial Regex HighlightIdentity();

    /// <summary>
    /// Finds one Quote, but only through its own entry and only for the reader who owns it.
    /// Another reader's Quote is not found rather than forbidden: it is not theirs to know about.
    /// </summary>
    public Task<Quote?> FindQuoteAsync(
        string readerId,
        Guid entryId,
        Guid quoteId,
        CancellationToken cancellationToken) =>
        database.Quotes
            .Where(quote => quote.Id == quoteId && quote.LibraryEntryId == entryId)
            .Where(quote => database.LibraryEntries
                .Any(entry => entry.Id == entryId && entry.ReaderId == readerId))
            .FirstOrDefaultAsync(cancellationToken);

    private Task<Quote?> FindQuoteByHighlightAsync(Guid entryId, string highlightId, CancellationToken cancellationToken) =>
        database.Quotes.FirstOrDefaultAsync(
            quote => quote.LibraryEntryId == entryId && quote.HighlightId == highlightId,
            cancellationToken);

    /// <summary>The Quote with its new note, or null when it was deleted between being found and changed.</summary>
    private async Task<Quote?> ReplaceNoteAsync(Quote quote, string? note, CancellationToken cancellationToken)
    {
        quote.Note = note;

        try
        {
            await database.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            database.Entry(quote).State = EntityState.Detached;

            return null;
        }

        return quote;
    }

    /// <summary>
    /// Text as Library keeps it: without null characters, which are not text and which the
    /// database cannot hold — refused, a device's queue would send them again forever — and none
    /// at all when nothing is left to show.
    /// </summary>
    private static string? Kept(string? text)
    {
        var kept = text?.Replace("\0", "", StringComparison.Ordinal);

        return string.IsNullOrWhiteSpace(kept) ? null : kept;
    }
}
