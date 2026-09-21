namespace ReadingTracker.Web.Services;

/// <summary>Why a pace figure is withheld. Each is a short reason the stripe says in place of the figure.</summary>
public enum PaceGap
{
    /// <summary>Nothing logged, so there is no reading to measure.</summary>
    NotStarted,

    /// <summary>Reading began today: a rate needs a day to have passed.</summary>
    StartedToday,

    /// <summary>The amount read is in percent, or in both units with nothing to add them together with, so it is not pages a day.</summary>
    NotInPages,

    /// <summary>No page count anywhere, so nothing to measure against or count down to.</summary>
    NoPageCount,

    /// <summary>The book is finished; there is nothing left to forecast.</summary>
    Finished,
}

/// <summary>
/// How a reader's reading of one book adds up over time: when it started, how long it has
/// taken, how fast it goes, and when it will be done at that speed. A pure calculation from
/// the LibraryEntry and its ReadingSessions, for the stripe on the Book page.
///
/// Every figure is given only when it means something: pages a day needs the amount read in
/// pages, a known length, and at least one calendar day gone; the forecast needs all of that
/// and a book not yet finished. A finished book's days and rate are frozen at Finished on.
/// </summary>
/// <param name="Started">The day of the earliest session, or the day the book was added when there is none.</param>
/// <param name="DaysReading">Calendar days from <paramref name="Started"/> to today, or to Finished on; never fewer than one.</param>
/// <param name="PagesRead">The amount read, when it is in pages.</param>
/// <param name="PageCount">The effective page count, when there is one.</param>
/// <param name="PagesADay">The rate, or null with <paramref name="NoRateBecause"/> saying why.</param>
/// <param name="FinishOn">The forecast, or null with <paramref name="NoForecastBecause"/> saying why.</param>
public sealed record ReadingPace(
    DateOnly Started,
    int DaysReading,
    decimal? PagesRead,
    int? PageCount,
    decimal? PagesADay,
    PaceGap? NoRateBecause,
    DateOnly? FinishOn,
    PaceGap? NoForecastBecause)
{
    /// <summary>
    /// Works the pace out as of <paramref name="today"/>, with session days taken in
    /// <paramref name="zone"/> — the reader's, since a session logged at 23:30 is that day's
    /// reading to them, whatever UTC says.
    /// </summary>
    public static ReadingPace Of(
        LibraryEntry entry,
        IReadOnlyList<ReadingSessionView> sessions,
        DateOnly today,
        TimeZoneInfo zone)
    {
        var started = sessions.Count == 0
            ? ReaderDays.Of(entry.AddedAt, zone)
            : sessions.Min(session => ReaderDays.Of(session.OccurredAt, zone));

        var finished = entry.Status == "Finished";
        var measuredTo = finished ? entry.FinishedOn ?? today : today;
        var daysGone = Math.Max(0, measuredTo.DayNumber - started.DayNumber);
        var daysReading = Math.Max(1, daysGone);

        var pagesRead = entry.Progress is { AmountRead: { } amount, Unit: "Pages" } ? amount : (decimal?)null;
        var pageCount = entry.EffectivePageCount;

        var noRate = NoRate(entry, sessions.Count, pagesRead, pageCount, daysGone, finished);
        var pagesADay = noRate is null ? pagesRead!.Value / daysReading : (decimal?)null;

        var noForecast = finished ? PaceGap.Finished : noRate;
        var finishOn = noForecast is null ? Forecast(today, pagesRead!.Value, pageCount!.Value, pagesADay!.Value) : (DateOnly?)null;

        return new ReadingPace(started, daysReading, pagesRead, pageCount, pagesADay, noRate, finishOn, noForecast);
    }

    private static PaceGap? NoRate(LibraryEntry entry, int sessions, decimal? pagesRead, int? pageCount, int daysGone, bool finished)
    {
        if (sessions == 0 || entry.Progress is null)
        {
            return PaceGap.NotStarted;
        }

        if (pagesRead is null)
        {
            return PaceGap.NotInPages;
        }

        if (pageCount is null)
        {
            return PaceGap.NoPageCount;
        }

        // A finished book's rate is frozen over the days it took, one at the least; an open
        // book started today has no rate yet, because "everything so far in no time" is not one.
        return daysGone == 0 && !finished ? PaceGap.StartedToday : null;
    }

    /// <summary>
    /// Today plus the days the rest will take at this rate, rounded up: a book that needs two
    /// and a bit more days is done on the third. A book already read to the end is done today.
    /// </summary>
    private static DateOnly Forecast(DateOnly today, decimal pagesRead, int pageCount, decimal pagesADay)
    {
        var remaining = Math.Max(0, pageCount - pagesRead);
        var days = (int)Math.Ceiling(remaining / pagesADay);

        return today.AddDays(days);
    }
}
