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
            // Run alongside Totals rather than after it — neither depends on the other's
            // result, and the Catalog round trip is the slower of the two.
            var booksTask = catalog.TryFindBooksAsync(
                [.. entries.Select(entry => entry.BookId).Distinct()],
                cancellationToken);
            var totalsTask = library.TotalsAsync([.. entries.Select(entry => entry.Id)], cancellationToken);

            await Task.WhenAll(booksTask, totalsTask);
            var books = await booksTask;
            var totals = await totalsTask;

            return Results.Ok(entries.Select(entry =>
            {
                var book = books.GetValueOrDefault(entry.BookId);

                return LibraryEntryResponse.From(
                    entry,
                    book,
                    Progress.Of(
                        totals.GetValueOrDefault(entry.Id, ReadingTotals.Nothing),
                        entry.TrackingMethod,
                        entry.EffectivePageCount(book?.TotalPages)));
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

        endpoints.MapDelete("/api/library/{entryId:guid}", async (
            Guid entryId,
            HttpRequest request,
            ReadingLibrary library,
            CancellationToken cancellationToken) =>
        {
            if (Reader.From(request) is not { } readerId)
            {
                return NotSaidWhoIsAsking();
            }

            return await library.RemoveAsync(readerId, entryId, cancellationToken)
                ? Results.NoContent()
                : Results.NotFound();
        })
        .WithName("RemoveFromLibrary");

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

        endpoints.MapPut("/api/library/{entryId:guid}/tracking-method", async (
            Guid entryId,
            SetTrackingMethodRequest body,
            HttpRequest request,
            ReadingLibrary library,
            CancellationToken cancellationToken) =>
        {
            if (Reader.From(request) is not { } readerId)
            {
                return NotSaidWhoIsAsking();
            }

            if (!Enum.TryParse<TrackingMethod>(body.TrackingMethod, ignoreCase: true, out var method))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["trackingMethod"] =
                    [
                        $"'{body.TrackingMethod}' is not a way of tracking a book. " +
                        $"Use one of: {string.Join(", ", Enum.GetNames<TrackingMethod>())}.",
                    ],
                });
            }

            var entry = await library.SetTrackingMethodAsync(readerId, entryId, method, cancellationToken);

            return entry is null ? Results.NotFound() : Results.Ok(LibraryEntryResponse.From(entry));
        })
        .WithName("SetTrackingMethod");

        endpoints.MapPut("/api/library/{entryId:guid}/page-count", async (
            Guid entryId,
            SetPageCountRequest body,
            HttpRequest request,
            ReadingLibrary library,
            CatalogClient catalog,
            CancellationToken cancellationToken) =>
        {
            if (Reader.From(request) is not { } readerId)
            {
                return NotSaidWhoIsAsking();
            }

            if (body.TotalPages is <= 0)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["totalPages"] = ["A book has at least one page. Leave it out to use the catalog's count."],
                });
            }

            var entry = await library.SetPageCountOverrideAsync(
                readerId,
                entryId,
                body.TotalPages,
                cancellationToken);

            if (entry is null)
            {
                return Results.NotFound();
            }

            var book = (await catalog.TryFindBooksAsync([entry.BookId], cancellationToken))
                .GetValueOrDefault(entry.BookId);

            return Results.Ok(LibraryEntryResponse.From(entry, book));
        })
        .WithName("SetPageCount");

        endpoints.MapPost("/api/library/{entryId:guid}/sessions", async (
            Guid entryId,
            SessionRequest body,
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

            var totalPages = await EffectivePageCountAsync(catalog, entry, cancellationToken);

            var (session, problem) = await library.LogSessionAsync(
                readerId,
                entryId,
                body.Amount,
                body.OccurredAt,
                body.DurationMinutes,
                cancellationToken);

            return problem is { } refused
                ? SessionRefused(refused)
                : Results.Created(
                    $"/api/library/{entryId}/sessions/{session!.Id}",
                    SessionResponse.From(session, entry.TrackingMethod, totalPages));
        })
        .WithName("LogReadingSession");

        endpoints.MapPut("/api/library/{entryId:guid}/sessions/{sessionId:guid}", async (
            Guid entryId,
            Guid sessionId,
            SessionRequest body,
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

            var totalPages = await EffectivePageCountAsync(catalog, entry, cancellationToken);

            var (session, problem) = await library.CorrectSessionAsync(
                readerId,
                entryId,
                sessionId,
                body.Amount,
                entry.TrackingMethod,
                body.OccurredAt,
                body.DurationMinutes,
                cancellationToken);

            return problem is { } refused
                ? SessionRefused(refused)
                : Results.Ok(SessionResponse.From(session!, entry.TrackingMethod, totalPages));
        })
        .WithName("CorrectReadingSession");

        endpoints.MapDelete("/api/library/{entryId:guid}/sessions/{sessionId:guid}", async (
            Guid entryId,
            Guid sessionId,
            HttpRequest request,
            ReadingLibrary library,
            CancellationToken cancellationToken) =>
        {
            if (Reader.From(request) is not { } readerId)
            {
                return NotSaidWhoIsAsking();
            }

            return await library.DeleteSessionAsync(readerId, entryId, sessionId, cancellationToken)
                ? Results.NoContent()
                : Results.NotFound();
        })
        .WithName("DeleteReadingSession");

        endpoints.MapGet("/api/library/{entryId:guid}/sessions", async (
            Guid entryId,
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

            var totalPagesTask = EffectivePageCountAsync(catalog, entry, cancellationToken);
            var sessionsTask = library.ListSessionsAsync(entryId, cancellationToken);

            await Task.WhenAll(totalPagesTask, sessionsTask);
            var totalPages = await totalPagesTask;
            var sessions = await sessionsTask;

            return Results.Ok(sessions.Select(session =>
                SessionResponse.From(session, entry.TrackingMethod, totalPages)));
        })
        .WithName("ListReadingSessions");
    }

    /// <summary>
    /// How long this reader's book is: their own page count where they set one, otherwise
    /// Catalog's. Null when neither knows — including when Catalog can't be reached, which
    /// skips the past-the-end check rather than stopping the reader recording what they read.
    /// Losing a validation beats losing the reading.
    /// </summary>
    private static async Task<int?> EffectivePageCountAsync(
        CatalogClient catalog,
        LibraryEntry entry,
        CancellationToken cancellationToken) =>
        entry.EffectivePageCount(
            (await catalog.TryFindBooksAsync([entry.BookId], cancellationToken))
                .GetValueOrDefault(entry.BookId)?.TotalPages);

    private static IResult SessionRefused(SessionProblem problem) => problem switch
    {
        SessionProblem.NotAnAmountOfReading => SessionRejected("Say how much you read — more than nothing."),

        // NoSuchEntry and NoSuchSession: not there, or not this reader's to know about.
        _ => Results.NotFound(),
    };

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

    /// <param name="PageCountOverride">This reader's own page count, null when they set none.</param>
    /// <param name="EffectivePageCount">
    /// The count everything is worked out from, so a client never has to decide for itself
    /// which of the two applies. Null when neither the reader nor Catalog knows.
    /// </param>
    private sealed record LibraryEntryResponse(
        Guid Id,
        Guid BookId,
        string Status,
        string TrackingMethod,
        DateTimeOffset AddedAt,
        int? PageCountOverride,
        int? EffectivePageCount,
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
                entry.PageCountOverride,
                entry.EffectivePageCount(book?.TotalPages),
                book is null ? null : BookDetailsResponse.From(book),
                progress is null ? null : ProgressResponse.From(progress));
    }

    /// <summary>
    /// How much of the book the reader has read. Added up from their sessions every time it is
    /// asked for, never stored, so it cannot disagree with the history it comes from.
    /// <paramref name="AmountRead"/> and <paramref name="Unit"/> are null together, when the
    /// reader has logged in both pages and percent and no page count exists to add them up with.
    /// </summary>
    private sealed record ProgressResponse(decimal? AmountRead, string? Unit, int? PercentComplete)
    {
        public static ProgressResponse From(ReadingProgress progress) =>
            new(progress.AmountRead, progress.Unit?.ToString(), progress.PercentComplete);
    }

    /// <summary>
    /// A session as it was recorded, plus the same reading expressed in whatever method the
    /// reader now tracks the book by. The recorded amount is never converted — it is the record
    /// of what the reader actually entered.
    /// </summary>
    private sealed record SessionResponse(
        Guid Id,
        decimal Amount,
        string Unit,
        DateTimeOffset OccurredAt,
        int? DurationMinutes,
        DisplayedAmountResponse? Displayed)
    {
        public static SessionResponse From(ReadingSession session, TrackingMethod method, int? totalPages) =>
            new(
                session.Id,
                session.Amount,
                session.Unit.ToString(),
                session.OccurredAt,
                session.DurationMinutes,
                DisplayedAmountResponse.Of(session, method, totalPages));
    }

    /// <summary>
    /// The session in the reader's current method. Null when that would need a page count
    /// nobody has: better to say nothing than to invent an amount.
    /// </summary>
    private sealed record DisplayedAmountResponse(decimal Amount, string Unit)
    {
        public static DisplayedAmountResponse? Of(ReadingSession session, TrackingMethod method, int? totalPages) =>
            UnitConversion.Convert(session.Amount, session.Unit, method, totalPages) is { } amount
                ? new DisplayedAmountResponse(amount, method.ToString())
                : null;
    }

    private sealed record SetTrackingMethodRequest(string? TrackingMethod);

    /// <summary>Null <paramref name="TotalPages"/> clears the override, going back to Catalog's count.</summary>
    private sealed record SetPageCountRequest(int? TotalPages);

    /// <summary>
    /// A stretch of reading as the reader describes it, whether they are logging it for the
    /// first time or correcting it afterwards. <paramref name="Amount"/> is how much they read,
    /// in whatever method they track this book by — not the pages it spanned.
    /// </summary>
    private sealed record SessionRequest(
        decimal Amount,
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
