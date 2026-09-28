using ReadingTracker.Library.Catalog;

namespace ReadingTracker.Library.Entries;

/// <summary>
/// A reader's reading over a stretch of time, across every book on their shelf: what the
/// reading-stats page draws its calendar, streaks and busiest days from, in one request.
///
/// Each session comes back as it happened — when, how many pages, how long, from where — rather
/// than added up into days here. A day is the reader's day, midnight to midnight where they are,
/// and only their browser knows where that is; so the browser does the adding up, and this
/// hands over a little more than a year's sessions at most, which is small. The books they were
/// against come too, by title, so the page needs nothing else.
/// </summary>
public static class ReadingHistoryEndpoints
{
    /// <summary>A year, and a day either side for the reader's timezone.</summary>
    private static readonly TimeSpan LongestSpan = TimeSpan.FromDays(368);

    public static void MapReadingHistoryEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/library/reading", async (
            DateTimeOffset? from,
            DateTimeOffset? to,
            HttpRequest request,
            ReadingLibrary library,
            CatalogClient catalog,
            CancellationToken cancellationToken) =>
        {
            if (Reader.From(request) is not { } readerId)
            {
                return LibraryProblems.NotSaidWhoIsAsking();
            }

            if (from is not { } start || to is not { } end || end <= start || end - start > LongestSpan)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["to"] = ["Give 'from' and 'to' as instants, 'to' after 'from', no more than a year apart."],
                });
            }

            var read = await library.ReadingBetweenAsync(readerId, start, end, cancellationToken);

            // One trip to Catalog for every book read in the stretch: its title for the page, and
            // its length, to say a session logged in percent in pages. Best-effort, as the shelf
            // is: without Catalog the reading still comes back, untitled, and percent of a book
            // whose length the reader did not set themselves has no pages.
            var books = await catalog.TryFindBooksAsync([.. read.Select(session => session.BookId).Distinct()], cancellationToken);

            return Results.Ok(new ReadingSpanResponse(
                [.. read
                    .DistinctBy(session => session.EntryId)
                    .Select(session => new ReadBookResponse(session.EntryId, books.GetValueOrDefault(session.BookId)?.Title))],
                [.. read.Select(session => new SessionInPagesResponse(
                    session.EntryId,
                    session.Session.OccurredAt,
                    UnitConversion.PagesRead(
                        session.Session.Amount,
                        session.Session.Unit,
                        LibraryEntry.EffectivePageCount(session.PageCountOverride, books.GetValueOrDefault(session.BookId)?.TotalPages)),
                    session.Session.DurationMinutes,
                    session.Session.Source == SessionSource.Device))]));
        })
        .WithName("ListReading");
    }

    private sealed record ReadingSpanResponse(
        IReadOnlyList<ReadBookResponse> Books,
        IReadOnlyList<SessionInPagesResponse> Sessions);

    /// <summary>A book read in the stretch. No title when Catalog could not be asked.</summary>
    private sealed record ReadBookResponse(Guid EntryId, string? Title);

    /// <summary>
    /// One stretch of reading, in pages. <paramref name="Pages"/> is null when it was logged in
    /// percent and nobody knows how long the book is: the reader read that day, but how much in
    /// pages is not known, and a guess would be worse than saying so.
    /// </summary>
    private sealed record SessionInPagesResponse(
        Guid EntryId,
        DateTimeOffset OccurredAt,
        decimal? Pages,
        int? DurationMinutes,
        bool FromDevice);
}
