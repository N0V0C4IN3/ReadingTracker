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

    public Task<LibraryEntry?> FindAsync(string readerId, Guid entryId, CancellationToken cancellationToken) =>
        database.LibraryEntries
            .FirstOrDefaultAsync(entry => entry.Id == entryId && entry.ReaderId == readerId, cancellationToken);

    public Task<List<ReadingSession>> ListSessionsAsync(Guid entryId, CancellationToken cancellationToken) =>
        database.ReadingSessions
            .Where(session => session.LibraryEntryId == entryId)
            .OrderByDescending(session => session.OccurredAt)
            .ToListAsync(cancellationToken);

    /// <summary>
    /// Records a stretch of reading. Starting a book the reader had only meant to read moves
    /// it to Reading: they have plainly started, and making them say so twice is busywork.
    /// Reaching the last page deliberately does not finish it — people stop before the end
    /// matter, and finishing is the reader's call.
    /// </summary>
    public async Task<(ReadingSession? Session, SessionProblem? Problem)> LogSessionAsync(
        string readerId,
        Guid entryId,
        decimal startPosition,
        decimal endPosition,
        DateTimeOffset? occurredAt,
        int? durationMinutes,
        int? totalPages,
        CancellationToken cancellationToken)
    {
        var entry = await FindAsync(readerId, entryId, cancellationToken);

        if (entry is null)
        {
            return (null, SessionProblem.NoSuchEntry);
        }

        if (SessionValidation.Check(startPosition, endPosition, entry.TrackingMethod, totalPages) is { } problem)
        {
            return (null, problem);
        }

        var now = clock.GetUtcNow();

        var session = new ReadingSession
        {
            Id = Guid.CreateVersion7(),
            LibraryEntryId = entry.Id,
            StartPosition = startPosition,
            EndPosition = endPosition,
            Unit = entry.TrackingMethod,
            OccurredAt = occurredAt ?? now,
            DurationMinutes = durationMinutes,
            LoggedAt = now,
        };

        database.ReadingSessions.Add(session);

        var previousStatus = entry.Status;

        if (entry.Status is ReadingStatus.WantToRead)
        {
            entry.Status = ReadingStatus.Reading;
        }

        await database.SaveChangesAsync(cancellationToken);

        if (previousStatus != entry.Status)
        {
            await events.PublishAsync(
                new ReadingStatusChanged(
                    entry.Id,
                    entry.ReaderId,
                    entry.BookId,
                    previousStatus.ToString(),
                    entry.Status.ToString(),
                    now),
                cancellationToken);
        }

        return (session, null);
    }

    /// <summary>
    /// Works out how far through each entry its reader is, from the latest ReadingSession.
    /// Progress is never stored: deriving it is what stops history and position disagreeing.
    /// </summary>
    public async Task<IReadOnlyDictionary<Guid, ReadingSession>> LatestSessionsAsync(
        IReadOnlyCollection<Guid> entryIds,
        CancellationToken cancellationToken)
    {
        if (entryIds.Count == 0)
        {
            return new Dictionary<Guid, ReadingSession>();
        }

        var sessions = await database.ReadingSessions
            .Where(session => entryIds.Contains(session.LibraryEntryId))
            .ToListAsync(cancellationToken);

        return sessions
            .GroupBy(session => session.LibraryEntryId)
            .ToDictionary(
                perEntry => perEntry.Key,
                // Latest by when the reading happened, falling back to when it was logged so
                // two sessions on the same day still have a definite order.
                perEntry => perEntry
                    .OrderByDescending(session => session.OccurredAt)
                    .ThenByDescending(session => session.LoggedAt)
                    .First());
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
