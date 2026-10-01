namespace ReadingTracker.Web.Services;

/// <summary>
/// One calendar day of a reader's history: its sessions, latest first, and what they add up to.
/// <paramref name="Pages"/> is the sum of what came to pages (<see cref="ReadingSessionView.Pages"/>:
/// pages as logged, and percent through the effective page count, as Library worked them out) and
/// null when nothing did; <paramref name="Percent"/>
/// is the sum of percent that could not be, and null when there is none; <paramref name="Minutes"/>
/// is the sum of the durations given, and null when none was.
/// </summary>
public sealed record SessionDay(
    DateOnly Day,
    IReadOnlyList<ReadingSessionView> Sessions,
    decimal? Pages,
    decimal? Percent,
    int? Minutes)
{
    /// <summary>Today and yesterday by name, every other day by its weekday.</summary>
    public string Name(DateOnly today) =>
        Day == today ? "Today"
        : Day == today.AddDays(-1) ? "Yesterday"
        : Day.ToString("dddd");

    /// <summary>The date, with the year only when it is not this one.</summary>
    public string Date(DateOnly today) => Day.Year == today.Year ? Day.ToString("d MMM") : Day.ToString("d MMM yyyy");
}

/// <summary>
/// A reader's history as days: their sessions grouped by the calendar day they happened on in
/// the reader's own time zone, latest day first and latest session first within a day. An
/// e-reader that reported twice in an evening, and a stretch logged by hand the same night, are
/// one day, not three entries saying the same date. A pure grouping, for the day cards.
/// </summary>
public static class SessionDays
{
    /// <summary>How many days show before the rest are behind "Show all": three weeks of daily reading, and a bit.</summary>
    public const int ShownAtFirst = 20;

    public static IReadOnlyList<SessionDay> Of(IReadOnlyList<ReadingSessionView> sessions, TimeZoneInfo zone) =>
        sessions
            .GroupBy(session => ReaderDays.Of(session.OccurredAt, zone))
            .OrderByDescending(day => day.Key)
            .Select(day => Summed(day.Key, day.OrderByDescending(session => session.OccurredAt).ToList()))
            .ToList();

    /// <summary>
    /// The days to show: all of them when asked, otherwise the latest <see cref="ShownAtFirst"/>.
    /// Whether there is anything to ask for is whether there are more days than that.
    /// </summary>
    public static IReadOnlyList<SessionDay> Shown(IReadOnlyList<SessionDay> days, bool all) =>
        all || days.Count <= ShownAtFirst ? days : days.Take(ShownAtFirst).ToList();

    public static bool Folded(IReadOnlyList<SessionDay> days) => days.Count > ShownAtFirst;

    private static SessionDay Summed(DateOnly day, IReadOnlyList<ReadingSessionView> sessions)
    {
        decimal? pages = null;
        decimal? percent = null;
        int? minutes = null;

        foreach (var session in sessions)
        {
            // A session with no pages was logged in percent of a book nobody knows the length of,
            // so all there is to add up is the percent.
            if (session.Pages is { } read)
            {
                pages = (pages ?? 0) + read;
            }
            else
            {
                percent = (percent ?? 0) + session.Amount;
            }

            if (session.DurationMinutes is { } lasted)
            {
                minutes = (minutes ?? 0) + lasted;
            }
        }

        return new SessionDay(day, sessions, pages, percent, minutes);
    }
}
