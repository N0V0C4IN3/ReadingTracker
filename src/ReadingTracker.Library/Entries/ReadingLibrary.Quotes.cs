using Microsoft.EntityFrameworkCore;

namespace ReadingTracker.Library.Entries;

/// <summary>The Quotes on a reader's entries (ADR-0017).</summary>
public sealed partial class ReadingLibrary
{
    /// <summary>
    /// Keeps a passage a device sent, or finds the one it already sent under the same
    /// <paramref name="highlightId"/> and takes its <paramref name="note"/> — the one way an edit
    /// made on the device reaches the tracker, so no note on the device is no note here. The
    /// passage, where it is and when it was first saved stand as first sent: the Book's words do
    /// not change, and a retried request must not be able to move them.
    /// </summary>
    public async Task<(Quote? Quote, QuoteSaveOutcome Outcome, QuoteProblem? Problem)> SaveQuoteAsync(
        string readerId,
        Guid entryId,
        string highlightId,
        string passage,
        string? note,
        decimal percent,
        string? chapter,
        DateTimeOffset? savedAt,
        CancellationToken cancellationToken)
    {
        var entry = await FindAsync(readerId, entryId, cancellationToken);

        if (entry is null)
        {
            return (null, default, QuoteProblem.NoSuchEntry);
        }

        if (await FindQuoteByHighlightAsync(entry.Id, highlightId, cancellationToken) is { } sent)
        {
            return (await TakeNoteAsync(sent, note, cancellationToken), QuoteSaveOutcome.Updated, null);
        }

        var quote = new Quote
        {
            Id = Guid.CreateVersion7(),
            LibraryEntryId = entry.Id,
            HighlightId = highlightId,
            Passage = passage,
            Note = note,
            Percent = percent,
            Chapter = chapter,
            // UTC for the same reason as a session's time: see LogSessionAsync.
            SavedAt = (savedAt ?? clock.GetUtcNow()).ToUniversalTime(),
        };

        database.Quotes.Add(quote);

        try
        {
            await database.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // The same highlight arrived twice at once — a device retrying a request that had not
            // actually failed — and the other got there first; the unique index said so. This one
            // is the resend it would have been a moment later.
            database.Entry(quote).State = EntityState.Detached;

            var first = await FindQuoteByHighlightAsync(entry.Id, highlightId, cancellationToken)
                ?? throw new InvalidOperationException("A Quote refused as a duplicate could not be found.");

            return (await TakeNoteAsync(first, note, cancellationToken), QuoteSaveOutcome.Updated, null);
        }

        return (quote, QuoteSaveOutcome.Created, null);
    }

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
    /// Changes the Reader's note on a Quote, or takes it away with null. The note is the only
    /// thing about a Quote that can change: the passage is the Book's words.
    /// </summary>
    public async Task<Quote?> NoteQuoteAsync(
        string readerId,
        Guid entryId,
        Guid quoteId,
        string? note,
        CancellationToken cancellationToken) =>
        await FindQuoteAsync(readerId, entryId, quoteId, cancellationToken) is { } quote
            ? await TakeNoteAsync(quote, note, cancellationToken)
            : null;

    /// <summary>
    /// Removes a Quote, at once, as deleting a ReadingSession does. It can come back only if its
    /// highlight is still on the device to send again (ADR-0017).
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
        await database.SaveChangesAsync(cancellationToken);

        return true;
    }

    /// <summary>
    /// Finds one Quote, but only through its own entry and only for the reader who owns it.
    /// Another reader's Quote is not found rather than forbidden: it is not theirs to know about.
    /// </summary>
    private Task<Quote?> FindQuoteAsync(
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

    private async Task<Quote> TakeNoteAsync(Quote quote, string? note, CancellationToken cancellationToken)
    {
        quote.Note = note;
        await database.SaveChangesAsync(cancellationToken);

        return quote;
    }

    /// <summary>
    /// An entry's Quotes in book order: by where each is in the Book, and two at the same place by
    /// when each was saved. The caller must have found the entry for this reader.
    /// </summary>
    public Task<List<Quote>> ListQuotesAsync(Guid entryId, CancellationToken cancellationToken) =>
        database.Quotes
            .Where(quote => quote.LibraryEntryId == entryId)
            .OrderBy(quote => quote.Percent)
            .ThenBy(quote => quote.SavedAt)
            .ToListAsync(cancellationToken);
}
