using ReadingTracker.Library.Catalog;

namespace ReadingTracker.Library.Entries;

public static class LibraryEndpoints
{
    public static void MapLibraryEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/library", async (
            string? status,
            HttpRequest request,
            ReadingLibrary library,
            CatalogClient catalog,
            CancellationToken cancellationToken) =>
        {
            if (Reader.From(request) is not { } readerId)
            {
                return NotSaidWhoIsAsking();
            }

            ReadingStatus? filter = null;

            if (!string.IsNullOrWhiteSpace(status))
            {
                if (!Enum.TryParse<ReadingStatus>(status, ignoreCase: true, out var parsed))
                {
                    return UnknownReadingStatus(status);
                }

                filter = parsed;
            }

            var entries = await library.ListAsync(readerId, filter, cancellationToken);

            // One trip to Catalog for the whole shelf, not one per book. Best-effort: if
            // Catalog is unreachable the entries still come back, just without book details.
            var books = await catalog.TryFindBooksAsync(
                [.. entries.Select(entry => entry.BookId).Distinct()],
                cancellationToken);

            var latest = await library.LatestSessionsAsync([.. entries.Select(entry => entry.Id)], cancellationToken);

            return Results.Ok(entries.Select(entry =>
            {
                var book = books.GetValueOrDefault(entry.BookId);

                return LibraryEntryResponse.From(
                    entry,
                    book,
                    Progress.Of(latest.GetValueOrDefault(entry.Id), book?.TotalPages));
            }));
        })
        .WithName("ListLibrary");

        endpoints.MapPost("/api/library", async (
            AddToLibraryRequest body,
            HttpRequest request,
            ReadingLibrary library,
            CatalogClient catalog,
            CancellationToken cancellationToken) =>
        {
            if (Reader.From(request) is not { } readerId)
            {
                return NotSaidWhoIsAsking();
            }

            // Confirm the Book exists before recording a relationship to it, so an entry can
            // never point at nothing.
            CatalogBook? book;

            try
            {
                book = await catalog.FindBookAsync(body.BookId, cancellationToken);
            }
            catch (HttpRequestException)
            {
                // Unlike listing, adding cannot degrade: without Catalog we cannot tell an
                // unknown book from an unreachable one, and guessing would create an entry
                // pointing at nothing.
                return Results.Problem(
                    title: "The catalog is temporarily unavailable",
                    detail: "The book could not be confirmed just now. Try again shortly.",
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            if (book is null)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    [nameof(body.BookId)] = ["No such book in the catalog."],
                });
            }

            var (entry, failure) = await library.AddAsync(readerId, body.BookId, cancellationToken);

            return failure switch
            {
                AddToLibraryFailure.AlreadyInLibrary => Results.Problem(
                    title: "That book is already in your library",
                    detail: "A book appears in a reader's library once.",
                    statusCode: StatusCodes.Status409Conflict),

                _ => Results.Created($"/api/library/{entry!.Id}", LibraryEntryResponse.From(entry)),
            };
        })
        .WithName("AddToLibrary");

        endpoints.MapPut("/api/library/{entryId:guid}/status", async (
            Guid entryId,
            SetStatusRequest body,
            HttpRequest request,
            ReadingLibrary library,
            CancellationToken cancellationToken) =>
        {
            if (Reader.From(request) is not { } readerId)
            {
                return NotSaidWhoIsAsking();
            }

            if (!Enum.TryParse<ReadingStatus>(body.Status, ignoreCase: true, out var status))
            {
                return UnknownReadingStatus(body.Status);
            }

            var entry = await library.SetStatusAsync(readerId, entryId, status, cancellationToken);

            return entry is null ? Results.NotFound() : Results.Ok(LibraryEntryResponse.From(entry));
        })
        .WithName("SetReadingStatus");

        endpoints.MapPost("/api/library/{entryId:guid}/sessions", async (
            Guid entryId,
            LogSessionRequest body,
            HttpRequest request,
            ReadingLibrary library,
            CatalogClient catalog,
            CancellationToken cancellationToken) =>
        {
            if (Reader.From(request) is not { } readerId)
            {
                return NotSaidWhoIsAsking();
            }

            if (await library.FindAsync(readerId, entryId, cancellationToken) is not { } entry)
            {
                return Results.NotFound();
            }

            // Needed only to check the session doesn't run past the end. If Catalog can't be
            // reached we skip that check rather than block the reader from recording what
            // they read — losing one validation beats losing the reading.
            var books = await catalog.TryFindBooksAsync([entry.BookId], cancellationToken);
            var totalPages = books.GetValueOrDefault(entry.BookId)?.TotalPages;

            var (session, problem) = await library.LogSessionAsync(
                readerId,
                entryId,
                body.StartPosition,
                body.EndPosition,
                body.OccurredAt,
                body.DurationMinutes,
                totalPages,
                cancellationToken);

            return problem switch
            {
                SessionProblem.NoSuchEntry => Results.NotFound(),
                SessionProblem.EndsBeforeItStarts => SessionRejected("A session cannot end before it starts."),
                SessionProblem.RunsPastTheEndOfTheBook => SessionRejected(
                    $"This book has {totalPages} pages, so the session cannot end past that."),
                SessionProblem.PositionOutOfRange => SessionRejected("That position is outside the book."),
                _ => Results.Created(
                    $"/api/library/{entryId}/sessions/{session!.Id}",
                    SessionResponse.From(session)),
            };
        })
        .WithName("LogReadingSession");

        endpoints.MapGet("/api/library/{entryId:guid}/sessions", async (
            Guid entryId,
            HttpRequest request,
            ReadingLibrary library,
            CancellationToken cancellationToken) =>
        {
            if (Reader.From(request) is not { } readerId)
            {
                return NotSaidWhoIsAsking();
            }

            if (await library.FindAsync(readerId, entryId, cancellationToken) is null)
            {
                return Results.NotFound();
            }

            var sessions = await library.ListSessionsAsync(entryId, cancellationToken);

            return Results.Ok(sessions.Select(SessionResponse.From));
        })
        .WithName("ListReadingSessions");
    }

    private static IResult SessionRejected(string detail) =>
        Results.ValidationProblem(new Dictionary<string, string[]> { ["session"] = [detail] });

    private static IResult UnknownReadingStatus(string? given) =>
        Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["status"] = [$"'{given}' is not a reading status. Use one of: {string.Join(", ", Enum.GetNames<ReadingStatus>())}."],
        });

    private static IResult NotSaidWhoIsAsking() =>
        Results.Problem(
            title: "Unknown reader",
            detail: $"The {Reader.HeaderName} header is missing. Requests reach this service through the Gateway.",
            statusCode: StatusCodes.Status401Unauthorized);

    private sealed record AddToLibraryRequest(Guid BookId);

    private sealed record SetStatusRequest(string? Status);

    private sealed record LibraryEntryResponse(
        Guid Id,
        Guid BookId,
        string Status,
        string TrackingMethod,
        DateTimeOffset AddedAt,
        BookDetailsResponse? Book,
        ProgressResponse? Progress)
    {
        public static LibraryEntryResponse From(
            LibraryEntry entry,
            CatalogBook? book = null,
            ReadingProgress? progress = null) =>
            new(
                entry.Id,
                entry.BookId,
                entry.Status.ToString(),
                entry.TrackingMethod.ToString(),
                entry.AddedAt,
                book is null ? null : BookDetailsResponse.From(book),
                progress is null ? null : ProgressResponse.From(progress));
    }

    /// <summary>
    /// Where the reader has got to. Derived from their latest session every time it is asked
    /// for, never stored, so it cannot disagree with the history it comes from.
    /// </summary>
    private sealed record ProgressResponse(decimal Position, string Unit, int? PercentComplete)
    {
        public static ProgressResponse From(ReadingProgress progress) =>
            new(progress.Position, progress.Unit.ToString(), progress.PercentComplete);
    }

    private sealed record SessionResponse(
        Guid Id,
        decimal StartPosition,
        decimal EndPosition,
        string Unit,
        DateTimeOffset OccurredAt,
        int? DurationMinutes)
    {
        public static SessionResponse From(ReadingSession session) =>
            new(
                session.Id,
                session.StartPosition,
                session.EndPosition,
                session.Unit.ToString(),
                session.OccurredAt,
                session.DurationMinutes);
    }

    private sealed record LogSessionRequest(
        decimal StartPosition,
        decimal EndPosition,
        DateTimeOffset? OccurredAt,
        int? DurationMinutes);

    /// <summary>
    /// Catalog's description of the book, passed through rather than stored, so it can never
    /// drift from what Catalog says. Absent when Catalog could not be reached.
    /// </summary>
    private sealed record BookDetailsResponse(
        string Title,
        IReadOnlyList<string> Authors,
        string? Isbn,
        string? CoverUrl,
        int? TotalPages)
    {
        public static BookDetailsResponse From(CatalogBook book) =>
            new(book.Title, book.Authors, book.Isbn, book.CoverUrl, book.TotalPages);
    }
}
