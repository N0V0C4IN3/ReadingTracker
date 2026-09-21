namespace ReadingTracker.Web.Services;

/// <summary>One calendar day's reading, in pages.</summary>
public sealed record DayPages(DateOnly Day, decimal Pages);

/// <summary>
/// Pages read per calendar day over the last <see cref="WindowDays"/> days, ending today, for
/// the chart on the Book page. A pure series from the entry's ReadingSessions: each session's
/// pages go to the day it happened on in the reader's own time zone, and a session logged in
/// percent is turned into pages through the effective page count. One that cannot be — percent
/// with no page count to go through — is left out and counted, so the chart can say so.
/// </summary>
/// <param name="Days">Exactly <see cref="WindowDays"/> days, oldest first, the last one today.</param>
/// <param name="LeftOut">Sessions inside the window that could not be counted in pages.</param>
public sealed record DailyPages(IReadOnlyList<DayPages> Days, int LeftOut)
{
    /// <summary>Three weeks: enough to see a habit, few enough that a bar is still a bar on a phone.</summary>
    public const int WindowDays = 21;

    public decimal Total => Days.Sum(day => day.Pages);

    public decimal Max => Days.Count == 0 ? 0 : Days.Max(day => day.Pages);

    /// <summary>How many of the days had any reading at all.</summary>
    public int ReadingDays => Days.Count(day => day.Pages > 0);

    public static DailyPages Of(
        IReadOnlyList<ReadingSessionView> sessions,
        int? effectivePageCount,
        DateOnly today,
        TimeZoneInfo zone)
    {
        var first = today.AddDays(1 - WindowDays);
        var pages = new decimal[WindowDays];
        var leftOut = 0;

        foreach (var session in sessions)
        {
            var day = DayOf(session.OccurredAt, zone);

            // Outside the window is neither drawn nor worth mentioning.
            if (day < first || day > today)
            {
                continue;
            }

            if (PagesOf(session, effectivePageCount) is not { } read)
            {
                leftOut++;
                continue;
            }

            pages[day.DayNumber - first.DayNumber] += read;
        }

        var days = new DayPages[WindowDays];

        for (var at = 0; at < WindowDays; at++)
        {
            days[at] = new DayPages(first.AddDays(at), pages[at]);
        }

        return new DailyPages(days, leftOut);
    }

    private static decimal? PagesOf(ReadingSessionView session, int? effectivePageCount) => session.Unit switch
    {
        "Pages" => session.Amount,
        "Percentage" when effectivePageCount is { } total => session.Amount * total / 100,
        _ => null,
    };

    private static DateOnly DayOf(DateTimeOffset instant, TimeZoneInfo zone) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, zone).DateTime);
}
