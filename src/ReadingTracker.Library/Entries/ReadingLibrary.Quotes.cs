using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace ReadingTracker.Library.Entries;

/// <summary>The Quotes on a reader's entries (ADR-0017).</summary>
public sealed partial class ReadingLibrary
{
    /// <summary>The longest passage kept: pages of a book, far past any highlight a reader makes.</summary>
    public const int LongestPassage = 20_000;

    /// <summary>The longest note kept: a reader's thoughts, not a second book.</summary>
    public const int LongestNote = 10_000;

    /// <summary>The longest chapter name kept: a title, and a long one.</summary>
    public const int LongestChapter = 500;

    /// <summary>
    /// Keeps a passage a device sent, or finds the one it already sent under the same
    /// <paramref name="highlightId"/> and replaces its note with <paramref name="note"/>: the one
    /// way an edit made on the device reaches the tracker, so no note on the device is no note
    /// here. The passage, where it is and when it was first saved stand as first sent: the Book's
    /// words do not change, and a retried request must not be able to move them.
    ///
    /// One statement, which keeps the Quote or changes its note as the database finds it, so a
    /// save meeting a retry of itself, or the Reader deleting the same Quote, is never a failure:
    /// whichever got there first stands, and this save is what it would have been a moment later.
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
        passage = Kept(passage);
        note = Kept(note);
        chapter = Kept(chapter);

        if (Refused(highlightId, passage, note, percent, chapter) is { } problem)
        {
            return (null, problem);
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

        try
        {
            // A highlight already sent changes its note and nothing else; xmax is 0 only on a row
            // this statement made, which is how it says whether the Quote is new.
            var kept = await database.Database
                .SqlQuery<KeptQuote>($"""
                    INSERT INTO library."Quotes"
                        ("Id", "LibraryEntryId", "HighlightId", "Passage", "Note", "Percent", "Chapter", "SavedAt")
                    VALUES
                        ({Guid.CreateVersion7()}, {entry.Id}, {highlightId}, {passage}, {Text(note)}, {percent!.Value}, {Text(chapter)}, {saved})
                    ON CONFLICT ("LibraryEntryId", "HighlightId") DO UPDATE SET "Note" = EXCLUDED."Note"
                    RETURNING "Id", "LibraryEntryId", "HighlightId", "Passage", "Note", "Percent", "Chapter", "SavedAt",
                        (xmax = 0) AS "Created"
                    """)
                .ToListAsync(cancellationToken);

            return (kept.Single().AsSaved(), null);
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.ForeignKeyViolation)
        {
            // The book left the shelf after it was found: there is no entry to keep it on.
            return (null, QuoteProblem.NoSuchEntry);
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
            .AsNoTracking()
            .Where(quote => quote.LibraryEntryId == entryId)
            .OrderBy(quote => quote.Percent)
            .ThenBy(quote => quote.SavedAt)
            .ThenBy(quote => quote.Id)
            .ToListAsync(cancellationToken);

    /// <summary>
    /// Changes the Reader's note on a Quote, or takes it away with null or blank; no change at
    /// all leaves it as it is. The note is the only thing about a Quote that can change: the
    /// passage is the Book's words. No such Quote — including one deleted while this was on its
    /// way — is <see cref="QuoteProblem.NoSuchQuote"/>.
    /// </summary>
    public async Task<(Quote? Quote, QuoteProblem? Problem)> ChangeQuoteNoteAsync(
        string readerId,
        Guid entryId,
        Guid quoteId,
        NoteChange? change,
        CancellationToken cancellationToken)
    {
        if (change is not null)
        {
            var note = Kept(change.Note);

            if (note?.Length > LongestNote)
            {
                return (null, QuoteProblem.NoteTooLong);
            }

            await Owned(readerId, entryId, quoteId)
                .ExecuteUpdateAsync(set => set.SetProperty(quote => quote.Note, note), cancellationToken);
        }

        var quote = await FindQuoteAsync(readerId, entryId, quoteId, cancellationToken);

        return quote is null ? (null, QuoteProblem.NoSuchQuote) : (quote, null);
    }

    /// <summary>
    /// Removes a Quote, at once, as deleting a ReadingSession does. It can come back only if its
    /// highlight is still on the device to send again (ADR-0017). False when there is no such
    /// Quote, including one another request deleted first.
    /// </summary>
    public async Task<bool> DeleteQuoteAsync(
        string readerId,
        Guid entryId,
        Guid quoteId,
        CancellationToken cancellationToken) =>
        await Owned(readerId, entryId, quoteId).ExecuteDeleteAsync(cancellationToken) > 0;

    /// <summary>
    /// Finds one Quote, but only through its own entry and only for the reader who owns it.
    /// Another reader's Quote is not found rather than forbidden: it is not theirs to know about.
    /// </summary>
    public Task<Quote?> FindQuoteAsync(
        string readerId,
        Guid entryId,
        Guid quoteId,
        CancellationToken cancellationToken) =>
        Owned(readerId, entryId, quoteId).AsNoTracking().FirstOrDefaultAsync(cancellationToken);

    /// <summary>
    /// What a highlight's identity may be made of: the characters that mean the same thing
    /// escaped or not, so it reaches Library exactly as the device made it, through the Gateway
    /// and anything else in front of it. A slash is the one that matters — a KOReader position is
    /// full of them, and an escaped one is left escaped by one hop and decoded by another. The
    /// device hashes or encodes what it builds the identity from. At most 200 characters, which is
    /// far more than an identity needs and well inside what the index keeping a highlight one
    /// Quote can hold. At least one letter or digit, since an address made only of dots ("." and
    /// "..") is folded away before it arrives; and \z rather than $, which would let a line break
    /// through at the end.
    /// </summary>
    [GeneratedRegex(@"^(?=[._~-]*[A-Za-z0-9])[A-Za-z0-9._~-]{1,200}\z")]
    private static partial Regex HighlightIdentity();

    /// <summary>What is wrong with a device's save before Library looks for anything, if anything is.</summary>
    private static QuoteProblem? Refused(string highlightId, string? passage, string? note, decimal? percent, string? chapter) =>
        !HighlightIdentity().IsMatch(highlightId) ? QuoteProblem.NotAHighlightId
        : passage is null ? QuoteProblem.NoPassage
        : passage.Length > LongestPassage ? QuoteProblem.PassageTooLong
        // Missing is refused too, rather than read as the start of the book.
        : percent is not (>= 0m and <= 100m) ? QuoteProblem.NotAPercentage
        : note?.Length > LongestNote ? QuoteProblem.NoteTooLong
        : chapter?.Length > LongestChapter ? QuoteProblem.ChapterTooLong
        : null;

    /// <summary>One Quote, through its own entry, for the reader who owns it, and no one else.</summary>
    private IQueryable<Quote> Owned(string readerId, Guid entryId, Guid quoteId) =>
        database.Quotes
            .Where(quote => quote.Id == quoteId && quote.LibraryEntryId == entryId)
            .Where(quote => database.LibraryEntries.Any(entry => entry.Id == entryId && entry.ReaderId == readerId));

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

    /// <summary>Text that may be none, for a statement written by hand: typed, so none is still text.</summary>
    private static NpgsqlParameter Text(string? text) =>
        new() { NpgsqlDbType = NpgsqlDbType.Text, Value = (object?)text ?? DBNull.Value };

    /// <summary>A Quote as the saving statement answers with it, and whether the statement made it.</summary>
    private sealed record KeptQuote(
        Guid Id,
        Guid LibraryEntryId,
        string HighlightId,
        string Passage,
        string? Note,
        decimal Percent,
        string? Chapter,
        DateTimeOffset SavedAt,
        bool Created)
    {
        public SavedQuote AsSaved() =>
            new(
                new Quote
                {
                    Id = Id,
                    LibraryEntryId = LibraryEntryId,
                    HighlightId = HighlightId,
                    Passage = Passage,
                    Note = Note,
                    Percent = Percent,
                    Chapter = Chapter,
                    SavedAt = SavedAt,
                },
                Created ? QuoteSaveOutcome.Created : QuoteSaveOutcome.Updated);
    }
}
