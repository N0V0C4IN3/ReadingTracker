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
public sealed class ReadingLibrary(LibraryDbContext database, TimeProvider clock)
{
    public Task<List<LibraryEntry>> ListAsync(string readerId, CancellationToken cancellationToken) =>
        database.LibraryEntries
            .Where(entry => entry.ReaderId == readerId)
            .OrderByDescending(entry => entry.AddedAt)
            .ToListAsync(cancellationToken);

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
