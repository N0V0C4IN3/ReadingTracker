namespace ReadingTracker.Web.Services;

/// <summary>What was read of one book on one day, all its sittings added up.</summary>
/// <param name="Pages">Null when some of it was logged in percent of a book of unknown length.</param>
public sealed record BookOnDay(Guid EntryId, decimal? Pages, int Minutes, bool FromDevice);

/// <summary>One day the reader read on, and what.</summary>
public sealed record ReadingDay(DateOnly Date, IReadOnlyList<BookOnDay> Books)
{
    /// <summary>The pages a day's square starts each deeper shade at, 2 to 4; below the first is 1.</summary>
    public static readonly IReadOnlyList<decimal> LevelsFrom = [15m, 30m, 60m];

    /// <summary>The pages that are known. A day with some unknown is at least this.</summary>
    public decimal Pages { get; } = Books.Sum(book => book.Pages ?? 0m);

    public int Minutes { get; } = Books.Sum(book => book.Minutes);

    /// <summary>Some of the day was logged in percent of a book nobody knows the length of.</summary>
    public bool SomePagesUnknown { get; } = Books.Any(book => book.Pages is null);

    /// <summary>
    /// How deep the day's square is coloured, 1 to 4: a little, a sitting, a good read, a big day.
    /// A day read in unknown pages is still a day read, so it is never 0.
    /// </summary>
    public int Level => 1 + LevelsFrom.Count(from => Pages >= from);
}

/// <summary>A run of days read one after another.</summary>
public sealed record Streak(DateOnly From, DateOnly To)
{
    public int Days => To.DayNumber - From.DayNumber + 1;
}

/// <summary>Pages a day, on average, on one day of the week.</summary>
public sealed record WeekdayPages(DayOfWeek Day, decimal Pages);

/// <summary>
/// A square on the calendar. <paramref name="Read"/> is null on a day with nothing read;
/// <paramref name="Ahead"/> is a day still to come, which has nothing to pick.
/// </summary>
public sealed record CalendarDay(DateOnly Date, ReadingDay? Read, bool Ahead);

/// <summary>
/// A column of the calendar, Monday to Sunday; a day outside the year is null.
/// </summary>
/// <param name="Month">The month's short name, over the week it starts in and over the first week.</param>
/// <param name="Recent">One of the weeks a phone shows before the whole year is asked for.</param>
/// <param name="Ahead">A week wholly still to come.</param>
public sealed record CalendarWeek(IReadOnlyList<CalendarDay?> Days, string? Month, bool Recent, bool Ahead);

/// <summary>
/// A reader's year of reading, day by day: the calendar the stats page draws, and what is worth
/// saying about it — streaks, the busiest day, the day of the week they read most on. All of it
/// worked out once, when the year is made.
///
/// Days are the reader's own, midnight to midnight where they are (see <see cref="ReaderDays"/>):
/// a session at 00:30 counts to the day it was read on, not to the day before in UTC. Worked out
/// here, in the browser, which is the only place that knows where the reader is.
/// </summary>
public sealed class ReadingYear
{
    /// <summary>The weeks a phone shows, up to the one today is in: about four months.</summary>
    public const int RecentWeeks = 17;

    public static readonly IReadOnlyList<DayOfWeek> MondayFirst =
        [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday];

    private readonly IReadOnlyDictionary<Guid, string> _titles;

    private ReadingYear(int year, DateOnly today, IReadOnlyDictionary<DateOnly, ReadingDay> days, IReadOnlyDictionary<Guid, string> titles)
    {
        Year = year;
        IsCurrent = today.Year == year;
        LastDay = IsCurrent ? today : new DateOnly(year, 12, 31);
        Days = days;
        _titles = titles;
        Weeks = WeeksOf(year, LastDay, days);
        Longest = StreaksIn(days.Keys).MaxBy(streak => streak.Days);
        Current = IsCurrent ? CurrentStreak(days, today) : null;
        BiggestDay = days.Values
            .OrderByDescending(day => day.Pages)
            .ThenBy(day => day.Date)
            .FirstOrDefault(day => day.Pages > 0);
        ByWeekday = WeekdaysOf(year, LastDay, days);
        FirstPicked = days.ContainsKey(LastDay) || days.Count == 0 ? LastDay : days.Keys.Max();
    }

    public int Year { get; }

    /// <summary>The days with something read, up to <see cref="LastDay"/>.</summary>
    public IReadOnlyDictionary<DateOnly, ReadingDay> Days { get; }

    /// <summary>Today in the year under way; the last of December in a year gone by.</summary>
    public DateOnly LastDay { get; }

    public bool IsCurrent { get; }

    /// <summary>The calendar: a column per week, from the week the year starts in to the week it ends in.</summary>
    public IReadOnlyList<CalendarWeek> Weeks { get; }

    /// <summary>The run going now — read today, or yesterday and not yet today. Null when there is none, or in a year gone by.</summary>
    public Streak? Current { get; }

    public Streak? Longest { get; }

    public int DaysRead => Days.Count;

    /// <summary>The days that have come so far: to today in this year, all of them in a year gone by.</summary>
    public int DaysSoFar => LastDay.DayOfYear;

    /// <summary>The day with the most pages, the earliest of any tie.</summary>
    public ReadingDay? BiggestDay { get; }

    /// <summary>
    /// Pages a day on each day of the week, Monday first, over the weeks so far: every Sunday of
    /// the year counts, the ones with nothing read as nothing.
    /// </summary>
    public IReadOnlyList<WeekdayPages> ByWeekday { get; }

    /// <summary>The day the page opens on: today when it has been read on, else the last day that was.</summary>
    public DateOnly FirstPicked { get; }

    /// <summary>The book's title, or just "A book" when Catalog could not say.</summary>
    public string TitleOf(Guid entryId) => _titles.GetValueOrDefault(entryId) ?? "A book";

    /// <summary>
    /// The year's reading from the stretch Library sent. Each session is placed on the day it
    /// happened in <paramref name="zone"/>; one that falls outside the year there, or after
    /// <paramref name="today"/>, is left out.
    /// </summary>
    public static ReadingYear From(int year, ReadingSpan span, DateOnly today, TimeZoneInfo zone)
    {
        var lastDay = today.Year == year ? today : new DateOnly(year, 12, 31);
        var days = span.Sessions
            .GroupBy(session => ReaderDays.Of(session.OccurredAt, zone))
            .Where(day => day.Key.Year == year && day.Key <= lastDay)
            .ToDictionary(
                day => day.Key,
                day => new ReadingDay(day.Key, [.. day
                    .GroupBy(session => session.EntryId)
                    .Select(book => new BookOnDay(
                        book.Key,
                        book.Any(session => session.Pages is null) ? null : book.Sum(session => session.Pages!.Value),
                        book.Sum(session => session.DurationMinutes ?? 0),
                        book.Any(session => session.FromDevice)))
                    .OrderByDescending(book => book.Pages ?? 0m)]));
        var titles = span.Books
            .Where(book => book.Title is not null)
            .ToDictionary(book => book.EntryId, book => book.Title!);

        return new ReadingYear(year, today, days, titles);
    }

    /// <summary>The range of instants a year covers where the reader is, to ask Library for.</summary>
    public static (DateTimeOffset From, DateTimeOffset To) Span(int year, TimeZoneInfo zone)
    {
        DateTimeOffset At(DateTime local) => new(local, zone.GetUtcOffset(local));
        return (At(new DateTime(year, 1, 1)), At(new DateTime(year + 1, 1, 1)));
    }

    private static IReadOnlyList<CalendarWeek> WeeksOf(int year, DateOnly lastDay, IReadOnlyDictionary<DateOnly, ReadingDay> days)
    {
        var first = new DateOnly(year, 1, 1);
        var last = new DateOnly(year, 12, 31);
        var firstMonday = first.AddDays(-(((int)first.DayOfWeek + 6) % 7));
        var lastWeek = (lastDay.DayNumber - firstMonday.DayNumber) / 7;

        var weeks = new List<CalendarWeek>();
        for (var (monday, index) = (firstMonday, 0); monday <= last; monday = monday.AddDays(7), index++)
        {
            var dates = Enumerable.Range(0, 7).Select(n => monday.AddDays(n)).Where(date => date.Year == year).ToList();
            var month = dates.FirstOrDefault(date => date.Day == 1 || (index == 0 && date == first));
            weeks.Add(new CalendarWeek(
                [.. Enumerable.Range(0, 7).Select(n => monday.AddDays(n)).Select(date => date.Year == year
                    ? new CalendarDay(date, days.GetValueOrDefault(date), date > lastDay)
                    : null)],
                month == default ? null : month.ToString("MMM"),
                index > lastWeek - RecentWeeks && index <= lastWeek,
                index > lastWeek));
        }

        return weeks;
    }

    private static IReadOnlyList<WeekdayPages> WeekdaysOf(int year, DateOnly lastDay, IReadOnlyDictionary<DateOnly, ReadingDay> days)
    {
        var soFar = Enumerable.Range(0, lastDay.DayOfYear).Select(n => new DateOnly(year, 1, 1).AddDays(n)).ToList();
        return [.. MondayFirst.Select(weekday =>
        {
            var dates = soFar.Where(date => date.DayOfWeek == weekday).ToList();
            var pages = dates.Sum(date => days.GetValueOrDefault(date)?.Pages ?? 0m);
            return new WeekdayPages(weekday, dates.Count == 0 ? 0m : Math.Round(pages / dates.Count));
        })];
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
