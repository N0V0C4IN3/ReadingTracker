namespace ReadingTracker.Web.Services;

/// <summary>
/// The reader's calendar. A session is stored as an instant, but a reader remembers reading by
/// the day, and their day is the one where they were — a session at half past midnight in Kyiv
/// belongs to that day, whatever UTC says — so everything that groups or counts by day goes
/// through here with the reader's zone.
/// </summary>
public static class ReaderDays
{
    public static DateOnly Of(DateTimeOffset instant, TimeZoneInfo zone) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, zone).DateTime);
}
