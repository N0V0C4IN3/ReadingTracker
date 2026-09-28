using ReadingTracker.Library.Catalog;

namespace ReadingTracker.Library.Entries;

/// <summary>
/// A reader's reading over a stretch of time, across every book on their shelf: what the
/// reading-stats page draws its calendar, streaks and busiest days from.
///
/// Each session comes back as it happened — when, how much, how long, from where — rather than
/// added up into days here. A day is the reader's day, midnight to midnight where they are,
/// and only their browser knows where that is; so the browser does the adding up, and this
/// hands over a little more than a year's sessions at most, which is small.
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
                return LibraryEndpoints.NotSaidWhoIsAsking();
            }

            if (from is not { } start || to is not { } end || end <= start || end - start > LongestSpan)
            {
                return Results.Problem(
                    title: "Say which stretch of time",
                    detail: "Give 'from' and 'to' as instants, 'to' after 'from', no more than a year apart.",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            var read = await library.ReadingBetweenAsync(readerId, start, end, cancellationToken);

            // Pages are what a day is measured in. A session logged in percent needs the book's
            // length to be said in pages, and only those books need asking Catalog about — a
            // reader who set their own page count has already answered.
            var lengths = await catalog.TryFindBooksAsync(
                [.. read
                    .Where(pair => pair.Session.Unit == TrackingMethod.Percentage && pair.Entry.PageCountOverride is null)
                    .Select(pair => pair.Entry.BookId)
                    .Distinct()],
                cancellationToken);

            return Results.Ok(read.Select(pair => new ReadingMomentResponse(
                pair.Entry.Id,
                pair.Session.OccurredAt,
                UnitConversion.Convert(
                    pair.Session.Amount,
                    pair.Session.Unit,
                    TrackingMethod.Pages,
                    pair.Entry.EffectivePageCount(lengths.GetValueOrDefault(pair.Entry.BookId)?.TotalPages)),
                pair.Session.DurationMinutes,
                pair.Session.Source.ToString())));
        })
        .WithName("ListReading");
    }

    /// <summary>
    /// One stretch of reading. <paramref name="Pages"/> is null when it was logged in percent
    /// and nobody knows how long the book is: the reader read that day, but how much in pages
    /// is not known, and a guess would be worse than saying so.
    /// </summary>
    private sealed record ReadingMomentResponse(
        Guid EntryId,
        DateTimeOffset OccurredAt,
        decimal? Pages,
        int? DurationMinutes,
        string Source);
}
