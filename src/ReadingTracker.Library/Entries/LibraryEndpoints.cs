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
            CatalogClient catalog,
            TimeProvider clock,
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

            if (body.FinishedOn is { } finishedOn)
            {
                if (status != ReadingStatus.Finished)
                {
                    return Results.ValidationProblem(new Dictionary<string, string[]>
                    {
                        ["finishedOn"] = ["A day finished only goes with the Finished status."],
                    });
                }

                if (finishedOn > DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime).AddDays(1))
                {
                    // A day's grace, because the reader's today may already be the server's tomorrow.
                    return Results.ValidationProblem(new Dictionary<string, string[]>
                    {
                        ["finishedOn"] = ["That day has not happened yet."],
                    });
                }
            }

            var entry = await library.SetStatusAsync(readerId, entryId, status, body.FinishedOn, cancellationToken);

            return entry is null
                ? Results.NotFound()
                : Results.Ok(await DescribeAsync(library, catalog, entry, cancellationToken));
        })
        .WithName("SetReadingStatus");

        // The yearly goal: how many books the reader means to finish this year, beside how many
        // they have. The count comes back even with no goal set, so the shelf can say "3 finished
        // this year" before there is anything to measure it against.
        endpoints.MapGet("/api/library/goals/{year:int}", async (
            int year,
            HttpRequest request,
            ReadingLibrary library,
            CancellationToken cancellationToken) =>
        {
            if (Reader.From(request) is not { } readerId)
            {
                return NotSaidWhoIsAsking();
            }

            var (books, finished) = await library.GoalAsync(readerId, year, cancellationToken);

            return Results.Ok(new ReadingGoalResponse(year, books, finished));
        })
        .WithName("GetReadingGoal");

        endpoints.MapPut("/api/library/goals/{year:int}", async (
            int year,
            SetGoalRequest body,
            HttpRequest request,
            ReadingLibrary library,
            CancellationToken cancellationToken) =>
        {
            if (Reader.From(request) is not { } readerId)
            {
                return NotSaidWhoIsAsking();
            }

            if (body.Books is not (>= 1 and <= 1000))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["books"] = ["A goal is between 1 and 1000 books."],
                });
            }

            var (books, finished) = await library.SetGoalAsync(readerId, year, body.Books.Value, cancellationToken);

            return Results.Ok(new ReadingGoalResponse(year, books, finished));
        })
        .WithName("SetReadingGoal");

        endpoints.MapDelete("/api/library/goals/{year:int}", async (
            int year,
            HttpRequest request,
            ReadingLibrary library,
            CancellationToken cancellationToken) =>
        {
            if (Reader.From(request) is not { } readerId)
            {
                return NotSaidWhoIsAsking();
            }

            await library.ClearGoalAsync(readerId, year, cancellationToken);

            return Results.NoContent();
        })
        .WithName("ClearReadingGoal");

        endpoints.MapPut("/api/library/{entryId:guid}/tracking-method", async (
            Guid entryId,
            SetTrackingMethodRequest body,
            HttpRequest request,
            ReadingLibrary library,
            CatalogClient catalog,
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

            return entry is null
                ? Results.NotFound()
                : Results.Ok(await DescribeAsync(library, catalog, entry, cancellationToken));
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

            return Results.Ok(await DescribeAsync(library, catalog, entry, cancellationToken));
        })
        .WithName("SetPageCount");

        endpoints.MapPut("/api/library/{entryId:guid}/book", async (
            Guid entryId,
            ChangeBookRequest body,
            HttpRequest request,
            ReadingLibrary library,
            CatalogClient catalog,
            CancellationToken cancellationToken) =>
        {
            if (Reader.From(request) is not { } readerId)
            {
                return NotSaidWhoIsAsking();
            }

            // As when adding: an entry must never point at nothing, so the Book is confirmed
            // first, and without Catalog there is no telling unknown from unreachable.
            CatalogBook? book;

            try
            {
                book = await catalog.FindBookAsync(body.BookId, cancellationToken);
            }
            catch (HttpRequestException)
            {
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

            var (entry, failure) = await library.ChangeBookAsync(readerId, entryId, body.BookId, cancellationToken);

            if (failure is AddToLibraryFailure.AlreadyInLibrary)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    [nameof(body.BookId)] = ["That edition is already on your shelf as its own entry."],
                });
            }

            if (entry is null)
            {
                return Results.NotFound();
            }

            return Results.Ok(await DescribeAsync(library, entry, book, cancellationToken));
        })
        .WithName("ChangeBook");

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

            // Catalog is asked about the book while the session is being written rather than
            // before it: neither answer needs the other, so the reader waits for the slower of
            // the two instead of for both in turn.
            var bookTask = catalog.TryFindBooksAsync([entry.BookId], cancellationToken);

            var (session, problem) = await library.LogSessionAsync(
                readerId,
                entryId,
                body.Amount,
                body.OccurredAt,
                body.DurationMinutes,
                cancellationToken);

            var book = (await bookTask).GetValueOrDefault(entry.BookId);

            return problem is { } refused
                ? SessionRefused(refused)
                : Results.Created(
                    $"/api/library/{entryId}/sessions/{session!.Id}",
                    new LoggedSessionResponse(
                        SessionResponse.From(
                            session!,
                            entry.TrackingMethod,
                            entry.EffectivePageCount(book?.TotalPages)),
                        await DescribeAsync(library, entry, book, cancellationToken)));
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

            var bookTask = catalog.TryFindBooksAsync([entry.BookId], cancellationToken);

            var (session, problem) = await library.CorrectSessionAsync(
                readerId,
                entryId,
                sessionId,
                body.Amount,
                entry.TrackingMethod,
                body.OccurredAt,
                body.DurationMinutes,
                cancellationToken);

            var book = (await bookTask).GetValueOrDefault(entry.BookId);

            return problem is { } refused
                ? SessionRefused(refused)
                : Results.Ok(new LoggedSessionResponse(
                    SessionResponse.From(
                        session!,
                        entry.TrackingMethod,
                        entry.EffectivePageCount(book?.TotalPages)),
                    await DescribeAsync(library, entry, book, cancellationToken)));
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

        // A device saying where the reader is. Not a session endpoint, though it may make one:
        // the device reports a position, and how much reading that was is Library's to work out.
        endpoints.MapPut("/api/library/{entryId:guid}/bookmark", async (
            Guid entryId,
            BookmarkRequest body,
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

            if (body.Percent is not { } percent)
            {
                return NotAPercentage();
            }

            // The book is needed before the report, not just after it: a first report is measured
            // against what the reader had already read, which takes the page count to express.
            CatalogBook? book;

            try
            {
                book = await catalog.FindBookAsync(entry.BookId, cancellationToken);
            }
            catch (HttpRequestException)
            {
                // Best-effort once the Bookmark exists, as every other answer is. Before it
                // exists, "Catalog could not say" must not be read as "the book has no length":
                // that would measure hand-logged pages from zero and count them twice, and the
                // Bookmark set by that mistake would never be measured again. The device tries
                // later; a device is built for that.
                if (entry.BookmarkPercent is null && entry.PageCountOverride is null)
                {
                    return Results.Problem(
                        title: "The book's length could not be looked up",
                        detail: "A first Bookmark report is measured against what has already been read, " +
                                "which needs the book's page count, and Catalog could not be reached. Try again shortly.",
                        statusCode: StatusCodes.Status503ServiceUnavailable);
                }

                book = null;
            }

            var (session, problem) = await library.ReportBookmarkAsync(
                readerId,
                entryId,
                percent,
                body.OccurredAt,
                book?.TotalPages,
                cancellationToken);

            return problem switch
            {
                BookmarkProblem.NotAPercentage => NotAPercentage(),
                BookmarkProblem.NoSuchEntry => Results.NotFound(),
                BookmarkProblem.ReportedMeanwhile => Results.Problem(
                    title: "Another report for this book arrived at the same time",
                    detail: "The Bookmark moved while this report was being applied, so this one was not. " +
                            "Report the current position again.",
                    statusCode: StatusCodes.Status409Conflict),
                _ => Results.Ok(new BookmarkReportResponse(
                    session is null
                        ? null
                        : SessionResponse.From(session, entry.TrackingMethod, entry.EffectivePageCount(book?.TotalPages)),
                    await DescribeAsync(library, entry, book, cancellationToken))),
            };
        })
        .WithName("ReportBookmark");

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
    /// leaves a session's amount unconverted rather than stopping the reader reading their own
    /// history.
    /// </summary>
    private static async Task<int?> EffectivePageCountAsync(
        CatalogClient catalog,
        LibraryEntry entry,
        CancellationToken cancellationToken) =>
        entry.EffectivePageCount(
            (await catalog.TryFindBooksAsync([entry.BookId], cancellationToken))
                .GetValueOrDefault(entry.BookId)?.TotalPages);

    /// <summary>
    /// One entry as a client has to draw it: what Library holds, the book Catalog describes, and
    /// the progress derived from the reader's sessions — the same shape the shelf is made of.
    ///
    /// Every change answers with this, so a client that has just changed something already knows
    /// where the book stands and has no reason to go back for the shelf. That second request is
    /// what a change used to cost, and on a phone the round trip is most of the waiting; asking
    /// for the whole shelf to find out about one book was the rest of it.
    ///
    /// Best-effort about Catalog, as listing is: a book that cannot be described still has a
    /// status and a progress.
    /// </summary>
    private static async Task<LibraryEntryResponse> DescribeAsync(
        ReadingLibrary library,
        CatalogClient catalog,
        LibraryEntry entry,
        CancellationToken cancellationToken)
    {
        // Neither answer depends on the other, and the Catalog round trip is the slower.
        var bookTask = catalog.TryFindBooksAsync([entry.BookId], cancellationToken);
        var totalsTask = library.TotalsAsync([entry.Id], cancellationToken);

        await Task.WhenAll(bookTask, totalsTask);

        return Describe(entry, (await bookTask).GetValueOrDefault(entry.BookId), await totalsTask);
    }

    /// <summary>The same, for a caller that has already been to Catalog for this book.</summary>
    private static async Task<LibraryEntryResponse> DescribeAsync(
        ReadingLibrary library,
        LibraryEntry entry,
        CatalogBook? book,
        CancellationToken cancellationToken) =>
        Describe(entry, book, await library.TotalsAsync([entry.Id], cancellationToken));

    private static LibraryEntryResponse Describe(
        LibraryEntry entry,
        CatalogBook? book,
        IReadOnlyDictionary<Guid, ReadingTotals> totals) =>
        LibraryEntryResponse.From(
            entry,
            book,
            Progress.Of(
                totals.GetValueOrDefault(entry.Id, ReadingTotals.Nothing),
                entry.TrackingMethod,
                entry.EffectivePageCount(book?.TotalPages)));

    private static IResult SessionRefused(SessionProblem problem) => problem switch
    {
        SessionProblem.NotAnAmountOfReading => SessionRejected("Say how much you read — more than nothing."),

        // NoSuchEntry and NoSuchSession: not there, or not this reader's to know about.
        _ => Results.NotFound(),
    };

    private static IResult NotAPercentage() =>
        Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["percent"] = ["Say where in the book the reader is, between 0 and 100 percent."],
        });

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

    private sealed record SetStatusRequest(string? Status, DateOnly? FinishedOn);

    private sealed record SetGoalRequest(int? Books);

    /// <summary>A year's goal, or null for none, beside how many books were finished in it.</summary>
    private sealed record ReadingGoalResponse(int Year, int? Books, int Finished);

    /// <param name="PageCountOverride">This reader's own page count, null when they set none.</param>
    /// <param name="EffectivePageCount">
    /// The count everything is worked out from, so a client never has to decide for itself
    /// which of the two applies. Null when neither the reader nor Catalog knows.
    /// </param>
    /// <param name="Bookmark">Where a device last said the reader is; null when none has said.</param>
    private sealed record LibraryEntryResponse(
        Guid Id,
        Guid BookId,
        string Status,
        string TrackingMethod,
        DateTimeOffset AddedAt,
        DateOnly? FinishedOn,
        int? PageCountOverride,
        int? EffectivePageCount,
        BookDetailsResponse? Book,
        ProgressResponse? Progress,
        BookmarkResponse? Bookmark)
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
                entry.FinishedOn,
                entry.PageCountOverride,
                entry.EffectivePageCount(book?.TotalPages),
                book is null ? null : BookDetailsResponse.From(book),
                progress is null ? null : ProgressResponse.From(progress),
                BookmarkResponse.Of(entry));
    }

    private sealed record BookmarkResponse(decimal Percent, DateTimeOffset ReportedAt)
    {
        public static BookmarkResponse? Of(LibraryEntry entry) =>
            entry is { BookmarkPercent: { } percent, BookmarkReportedAt: { } reportedAt }
                ? new BookmarkResponse(percent, reportedAt)
                : null;
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
    /// <param name="Source">Whether the reader typed this or a device reported it.</param>
    private sealed record SessionResponse(
        Guid Id,
        decimal Amount,
        string Unit,
        string Source,
        DateTimeOffset OccurredAt,
        int? DurationMinutes,
        DisplayedAmountResponse? Displayed)
    {
        public static SessionResponse From(ReadingSession session, TrackingMethod method, int? totalPages) =>
            new(
                session.Id,
                session.Amount,
                session.Unit.ToString(),
                session.Source.ToString(),
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

    /// <summary>
    /// What comes back for a stretch of reading: the session as it was recorded, and the book as
    /// it now stands. Progress is derived from the sessions, so logging one moves it — and it is
    /// the card the reader is looking at, not the session.
    /// </summary>
    private sealed record LoggedSessionResponse(SessionResponse Session, LibraryEntryResponse Entry);

    /// <summary>
    /// What comes back for a Bookmark report: the session it amounted to — null when the reader
    /// went backwards or nowhere, which is not reading — and the book as it now stands.
    /// </summary>
    private sealed record BookmarkReportResponse(SessionResponse? Session, LibraryEntryResponse Entry);

    /// <summary>
    /// Where a device says the reader is, as a percentage of the book, and when that was — the
    /// time of the reading, which on a device that syncs later is not the time of the report.
    /// </summary>
    private sealed record BookmarkRequest(decimal? Percent, DateTimeOffset? OccurredAt);

    private sealed record SetTrackingMethodRequest(string? TrackingMethod);

    /// <summary>Null <paramref name="TotalPages"/> clears the override, going back to Catalog's count.</summary>
    private sealed record SetPageCountRequest(int? TotalPages);

    private sealed record ChangeBookRequest(Guid BookId);

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
