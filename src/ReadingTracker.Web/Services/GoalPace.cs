namespace ReadingTracker.Web.Services;

/// <summary>
/// Where a reader stands against the year's goal today: the number that would be on pace by
/// now, and a word about it. Pace is the goal spread evenly over the year's days and rounded
/// up — "on pace" on a given day means the book that day's share belongs to is done.
/// </summary>
public sealed record GoalPace(int OnPaceToday, string Label)
{
    public static GoalPace Of(int finished, int books, DateOnly today)
    {
        var daysInYear = DateTime.IsLeapYear(today.Year) ? 366 : 365;
        var onPace = (int)Math.Ceiling(books * (double)today.DayOfYear / daysInYear);

        var label = finished >= books ? "Done for the year"
            : finished > onPace ? "Ahead of pace"
            : finished == onPace ? "On pace"
            : $"{onPace - finished} behind pace";

        return new(onPace, label);
    }
}
