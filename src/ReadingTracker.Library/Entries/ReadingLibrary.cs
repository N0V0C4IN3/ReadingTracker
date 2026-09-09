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
    /// Changes how a reader tracks one book. Nothing they have logged is touched: sessions keep
    /// the unit they were recorded in and are converted on the way out, so a reader can change
    /// their mind as often as they like without losing what they actually entered.
    /// </summary>
    public async Task<LibraryEntry?> SetTrackingMethodAsync(
        string readerId,
        Guid entryId,
        TrackingMethod method,
        CancellationToken cancellationToken)
    {
        var entry = await FindAsync(readerId, entryId, cancellationToken);

        if (entry is null)
        {
            return null;
        }

        if (entry.TrackingMethod == method)
        {
            return entry;
        }

        entry.TrackingMethod = method;
        await database.SaveChangesAsync(cancellationToken);

        return entry;
    }

    /// <summary>
    /// Takes a book off a reader's shelf. Its ReadingSessions go with it — the database cascades
    /// them — so nothing is left pointing at an entry that no longer exists. The Book itself is
    /// Catalog's and is untouched, so the reader can add it again and start fresh.
    /// </summary>
    public async Task<bool> RemoveAsync(string readerId, Guid entryId, CancellationToken cancellationToken)
    {
        var entry = await FindAsync(readerId, entryId, cancellationToken);

        if (entry is null)
        {
            return false;
        }

        database.LibraryEntries.Remove(entry);
        await database.SaveChangesAsync(cancellationToken);

        return true;
    }

    /// <summary>
    /// Sets or clears this reader's own page count for a book. Pass null to go back to whatever
    /// Catalog says. Nothing is written back to Catalog: the shared Book is not this reader's
    /// to correct.
    /// </summary>
    public async Task<LibraryEntry?> SetPageCountOverrideAsync(
        string readerId,
        Guid entryId,
        int? pageCount,
        CancellationToken cancellationToken)
    {
        var entry = await FindAsync(readerId, entryId, cancellationToken);

        if (entry is null)
        {
            return null;
        }

        entry.PageCountOverride = pageCount;
        await database.SaveChangesAsync(cancellationToken);

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
        decimal amount,
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

        if (SessionValidation.Check(amount, entry.TrackingMethod, totalPages) is { } problem)
        {
            return (null, problem);
        }

        var now = clock.GetUtcNow();

        var session = new ReadingSession
        {
            Id = Guid.CreateVersion7(),
            LibraryEntryId = entry.Id,
            Amount = amount,
            Unit = entry.TrackingMethod,

            // Normalised to UTC rather than stored as it arrived. Postgres `timestamp with time
            // zone` accepts only a zero offset through Npgsql, so a reader in any zone east or
            // west of UTC — sending the perfectly valid "2026-09-08T00:00:00+03:00" their browser
            // produced — would otherwise get a 500 rather than a logged session. Converting keeps
            // the instant identical; only the offset it is written with changes.
            OccurredAt = (occurredAt ?? now).ToUniversalTime(),
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
    /// Changes a session the reader already logged. The correction is stated in
    /// <paramref name="unit"/> — the method the reader is tracking by now, which is the one the
    /// history they are looking at is written in — and the session is re-stamped with it. A
    /// correction is the reader saying what they read, so it is recorded in the unit they said
    /// it in; only a *change of tracking method* leaves the record alone and converts on the
    /// way out.
    ///
    /// Leaving <paramref name="occurredAt"/> out keeps the session where it is in time, since a
    /// session that has already happened cannot stop having happened.
    /// </summary>
    public async Task<(ReadingSession? Session, SessionProblem? Problem)> CorrectSessionAsync(
        string readerId,
        Guid entryId,
        Guid sessionId,
        decimal amount,
        TrackingMethod unit,
        DateTimeOffset? occurredAt,
        int? durationMinutes,
        int? totalPages,
        CancellationToken cancellationToken)
    {
        var session = await FindSessionAsync(readerId, entryId, sessionId, cancellationToken);

        if (session is null)
        {
            return (null, SessionProblem.NoSuchSession);
        }

        if (SessionValidation.Check(amount, unit, totalPages) is { } problem)
        {
            // Rejected outright, so the session the reader already had is left alone.
            return (null, problem);
        }

        session.Amount = amount;
        session.Unit = unit;
        session.DurationMinutes = durationMinutes;
        // UTC for the same reason as logging one: see LogSessionAsync.
        session.OccurredAt = (occurredAt ?? session.OccurredAt).ToUniversalTime();

        await database.SaveChangesAsync(cancellationToken);

        return (session, null);
    }

    /// <summary>
    /// Removes a session. Progress is derived, so it follows on its own: the reader's total
    /// drops by what this session recorded, or there is no total left at all when it was the
    /// only one.
    /// </summary>
    public async Task<bool> DeleteSessionAsync(
        string readerId,
        Guid entryId,
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        var session = await FindSessionAsync(readerId, entryId, sessionId, cancellationToken);

        if (session is null)
        {
            return false;
        }

        database.ReadingSessions.Remove(session);
        await database.SaveChangesAsync(cancellationToken);

        return true;
    }

    /// <summary>
    /// Finds one session, but only through its own entry and only for the reader who owns it.
    /// Another reader's session is not found rather than forbidden: it is not theirs to know about.
    /// </summary>
    private Task<ReadingSession?> FindSessionAsync(
        string readerId,
        Guid entryId,
        Guid sessionId,
        CancellationToken cancellationToken) =>
        database.ReadingSessions
            .Where(session => session.Id == sessionId && session.LibraryEntryId == entryId)
            .Where(session => database.LibraryEntries
                .Any(entry => entry.Id == entryId && entry.ReaderId == readerId))
            .FirstOrDefaultAsync(cancellationToken);

    /// <summary>
    /// Adds up what each reader has read of each entry. Progress is never stored: deriving it
    /// is what stops the history and the total disagreeing.
    ///
    /// Totals are kept apart by unit rather than added up here, because pages and percentages
    /// can only be added together with a page count — which belongs to the entry, and which the
    /// caller has already gone to Catalog for.
    /// </summary>
    public async Task<IReadOnlyDictionary<Guid, ReadingTotals>> TotalsAsync(
        IReadOnlyCollection<Guid> entryIds,
        CancellationToken cancellationToken)
    {
        if (entryIds.Count == 0)
        {
            return new Dictionary<Guid, ReadingTotals>();
        }

        var totals = await database.ReadingSessions
            .Where(session => entryIds.Contains(session.LibraryEntryId))
            .GroupBy(session => new { session.LibraryEntryId, session.Unit })
            .Select(perUnit => new
            {
                perUnit.Key.LibraryEntryId,
                perUnit.Key.Unit,
                Amount = perUnit.Sum(session => session.Amount),
            })
            .ToListAsync(cancellationToken);

        return totals
            .GroupBy(total => total.LibraryEntryId)
            .ToDictionary(
                perEntry => perEntry.Key,
                perEntry => perEntry.Aggregate(
                    ReadingTotals.Nothing,
                    (running, total) => running.Plus(total.Amount, total.Unit)));
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
