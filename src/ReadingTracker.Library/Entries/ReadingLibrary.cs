using Microsoft.EntityFrameworkCore;
using ReadingTracker.Library.Persistence;

namespace ReadingTracker.Library.Entries;

/// <summary>Why a book could not be added to a library.</summary>
public enum AddToLibraryFailure
{
    /// <summary>Catalog has no such Book, so an entry would point at nothing.</summary>
    NoSuchBook,

    /// <summary>The reader already has this Book; one entry per book per reader.</summary>
    AlreadyInLibrary,
}

/// <summary>
/// One reader's collection of books. Holds the reader's relationship to a Book — status and
/// tracking method — and never a copy of the Book itself, which stays Catalog's to own.
/// </summary>
public sealed class ReadingLibrary(LibraryDbContext database, ILibraryEvents events, TimeProvider clock)
{
    public Task<List<LibraryEntry>> ListAsync(
        string readerId,
        ReadingStatus? withStatus,
        CancellationToken cancellationToken) =>
        database.LibraryEntries
            .Where(entry => entry.ReaderId == readerId)
            .Where(entry => withStatus == null || entry.Status == withStatus)
            .OrderByDescending(entry => entry.AddedAt)
            .ToListAsync(cancellationToken);

    /// <summary>
    /// Moves an entry to a new ReadingStatus. Returns null when the entry is not in this
    /// reader's library — including when it is in someone else's, which is not this reader's
    /// business to know about.
    /// </summary>
    public async Task<LibraryEntry?> SetStatusAsync(
        string readerId,
        Guid entryId,
        ReadingStatus status,
        CancellationToken cancellationToken)
    {
        var entry = await database.LibraryEntries
            .FirstOrDefaultAsync(e => e.Id == entryId && e.ReaderId == readerId, cancellationToken);

        if (entry is null)
        {
            return null;
        }

        var previous = entry.Status;

        if (previous == status)
        {
            // Nothing changed, so there is nothing to save and nothing to announce.
            return entry;
        }

        entry.Status = status;
        await database.SaveChangesAsync(cancellationToken);

        await events.PublishAsync(
            new ReadingStatusChanged(
                entry.Id,
                entry.ReaderId,
                entry.BookId,
                previous.ToString(),
                status.ToString(),
                clock.GetUtcNow()),
            cancellationToken);

        return entry;
    }

    /// <summary>
    /// Adds a Book to a reader's library. The caller must have confirmed with Catalog that the
    /// Book exists; this method only enforces the one-entry-per-book rule.
    /// </summary>
    public async Task<(LibraryEntry? Entry, AddToLibraryFailure? Failure)> AddAsync(
        string readerId,
        Guid bookId,
        CancellationToken cancellationToken)
    {
        var entry = new LibraryEntry
        {
            Id = Guid.CreateVersion7(),
            ReaderId = readerId,
            BookId = bookId,
            Status = ReadingStatus.WantToRead,
            TrackingMethod = TrackingMethod.Pages,
            AddedAt = clock.GetUtcNow(),
        };

        database.LibraryEntries.Add(entry);

        try
        {
            await database.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // The unique index caught it, including when two requests raced.
            database.Entry(entry).State = EntityState.Detached;
            return (null, AddToLibraryFailure.AlreadyInLibrary);
        }

        return (entry, null);
    }
}
