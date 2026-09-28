namespace ReadingTracker.Web.Services;

/// <summary>What was read of one book on one day, all its sittings added up.</summary>
/// <param name="Pages">Null when some of it was logged in percent of a book of unknown length.</param>
public sealed record BookOnDay(Guid EntryId, decimal? Pages, int Minutes, bool FromDevice);

/// <summary>One day the reader read on, and what.</summary>
public sealed record ReadingDay(DateOnly Date, IReadOnlyList<BookOnDay> Books)
{
    /// <summary>The pages that are known. A day with some unknown is at least this.</summary>
    public decimal Pages => Books.Sum(book => book.Pages ?? 0m);

    public int Minutes => Books.Sum(book => book.Minutes);

    /// <summary>
    /// How deep the day's square is coloured, 1 to 4: a little, a sitting, a good read, a big day.
    /// A day read in unknown pages is still a day read, so it is never 0.
    /// </summary>
    public int Level => Pages switch
    {
        < 15m => 1,
        < 30m => 2,
        < 60m => 3,
        _ => 4,
    };
}

/// <summary>A run of days read one after another.</summary>
public sealed record Streak(DateOnly From, DateOnly To)
{
    public int Days => To.DayNumber - From.DayNumber + 1;
}

/// <summary>
/// A reader's year of reading, day by day: the calendar the stats page draws, and what is worth
/// saying about it — streaks, the busiest day, the day of the week they read most on.
///
/// Days are the reader's own, midnight to midnight where they are: a session at 00:30 counts to
/// the day it was read on, not to the day before in UTC. Worked out here, in the browser, which
/// is the only place that knows where the reader is.
/// </summary>
public sealed class ReadingYear
{
    private ReadingYear(int year, DateOnly today, IReadOnlyDictionary<DateOnly, ReadingDay> days)
    {
        Year = year;
        Days = days;
        LastDay = today.Year == year ? today : new DateOnly(year, 12, 31);
        IsCurrent = today.Year == year;
        Weeks = WeeksOf(year);
        Longest = StreaksIn(days.Keys).MaxBy(streak => streak.Days);
        Current = IsCurrent ? CurrentStreak(days, today) : null;
    }

    public int Year { get; }

    public IReadOnlyDictionary<DateOnly, ReadingDay> Days { get; }

    /// <summary>Today in the year under way; the last of December in a year gone by.</summary>
    public DateOnly LastDay { get; }

    public bool IsCurrent { get; }

    /// <summary>
    /// The calendar: a column per week, Monday to Sunday, from the week the year starts in to
    /// the week it ends in. A day that falls outside the year is null.
    /// </summary>
    public IReadOnlyList<IReadOnlyList<DateOnly?>> Weeks { get; }

    /// <summary>The run going now — read today, or yesterday and not yet today. Null when there is none, or in a year gone by.</summary>
    public Streak? Current { get; }

    public Streak? Longest { get; }

    public int DaysRead => Days.Count;

    /// <summary>The days that have come so far: to today in this year, all of them in a year gone by.</summary>
    public int DaysSoFar => LastDay.DayOfYear;

    /// <summary>The day with the most pages, the earliest of any tie.</summary>
    public ReadingDay? BiggestDay => Days.Values
        .OrderByDescending(day => day.Pages)
        .ThenBy(day => day.Date)
        .FirstOrDefault(day => day.Pages > 0);

    /// <summary>
    /// Pages a day on each day of the week, Monday first, over the weeks so far: every Sunday of
    /// the year counts, the ones with nothing read as nothing.
    /// </summary>
    public IReadOnlyList<(DayOfWeek Day, decimal Pages)> ByWeekday =>
        [.. MondayFirst.Select(weekday =>
        {
            var dates = Enumerable.Range(1, DaysSoFar)
                .Select(n => new DateOnly(Year, 1, 1).AddDays(n - 1))
                .Where(date => date.DayOfWeek == weekday)
                .ToList();
            var pages = dates.Sum(date => Days.GetValueOrDefault(date)?.Pages ?? 0m);
            return (weekday, dates.Count == 0 ? 0m : Math.Round(pages / dates.Count));
        })];

    public static readonly IReadOnlyList<DayOfWeek> MondayFirst =
        [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday];

    /// <summary>
    /// The year's reading from the sessions in it. Each is placed on the day it happened in
    /// <paramref name="zone"/>; one that falls outside the year there is left out.
    /// </summary>
    public static ReadingYear From(int year, IEnumerable<ReadingMoment> moments, DateOnly today, TimeZoneInfo zone)
    {
        var days = moments
            .GroupBy(moment => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(moment.OccurredAt, zone).DateTime))
            .Where(day => day.Key.Year == year)
            .ToDictionary(
                day => day.Key,
                day => new ReadingDay(day.Key, [.. day
                    .GroupBy(moment => moment.EntryId)
                    .Select(book => new BookOnDay(
                        book.Key,
                        book.Any(moment => moment.Pages is null) ? null : book.Sum(moment => moment.Pages!.Value),
                        book.Sum(moment => moment.DurationMinutes ?? 0),
                        book.Any(moment => moment.Source == "Device")))
                    .OrderByDescending(book => book.Pages ?? 0m)]));

        return new ReadingYear(year, today, days);
    }

    /// <summary>The range of instants a year covers where the reader is, to ask Library for.</summary>
    public static (DateTimeOffset From, DateTimeOffset To) Span(int year, TimeZoneInfo zone)
    {
        DateTimeOffset At(DateTime local) => new(local, zone.GetUtcOffset(local));
        return (At(new DateTime(year, 1, 1)), At(new DateTime(year + 1, 1, 1)));
    }

    private static IReadOnlyList<IReadOnlyList<DateOnly?>> WeeksOf(int year)
    {
        var first = new DateOnly(year, 1, 1);
        var last = new DateOnly(year, 12, 31);
        var monday = first.AddDays(-(((int)first.DayOfWeek + 6) % 7));

        var weeks = new List<IReadOnlyList<DateOnly?>>();
        for (var start = monday; start <= last; start = start.AddDays(7))
        {
            weeks.Add([.. Enumerable.Range(0, 7).Select(n => start.AddDays(n)).Select(day => day.Year == year ? day : (DateOnly?)null)]);
        }

        return weeks;
    }

    private static IEnumerable<Streak> StreaksIn(IEnumerable<DateOnly> read)
    {
        Streak? run = null;
        foreach (var day in read.Order())
        {
            if (run is not null && day == run.To.AddDays(1))
            {
                run = run with { To = day };
                continue;
            }

            if (run is not null)
            {
                yield return run;
            }

            run = new Streak(day, day);
        }

        if (run is not null)
        {
            yield return run;
        }
    }

    private static Streak? CurrentStreak(IReadOnlyDictionary<DateOnly, ReadingDay> days, DateOnly today)
    {
        var end = days.ContainsKey(today) ? today : today.AddDays(-1);
        if (!days.ContainsKey(end))
        {
            return null;
        }

        var start = end;
        while (days.ContainsKey(start.AddDays(-1)))
        {
            start = start.AddDays(-1);
        }

        return new Streak(start, end);
    }
}
