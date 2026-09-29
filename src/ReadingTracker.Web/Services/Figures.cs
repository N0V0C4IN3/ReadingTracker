namespace ReadingTracker.Web.Services;

/// <summary>How the page writes the numbers Library hands it.</summary>
public static class Figures
{
    /// <summary>A whole number as itself, and anything else to one decimal: "12", "12.5", never "12.0".</summary>
    public static string Trim(decimal value) =>
        value == decimal.Truncate(value) ? decimal.Truncate(value).ToString() : value.ToString("0.#");

    /// <summary>
    /// Pages to the nearest whole one — "1 page", "12 pages" — and a sliver of one, a tenth of a
    /// percent of a long book say, as "under a page" rather than "0 pages".
    /// </summary>
    public static string Pages(decimal pages)
    {
        var whole = Math.Round(pages, MidpointRounding.AwayFromZero);
        return whole switch
        {
            0m when pages > 0m => "under a page",
            1m => "1 page",
            _ => $"{whole} pages",
        };
    }

    public static string Days(int days) => days == 1 ? "1 day" : $"{days} days";

    /// <summary>
    /// How long ago, in the coarse terms a shelf needs — "today", "yesterday", "5 days ago",
    /// "3 weeks ago", "2 months ago", "1 year ago". Whole elapsed days rather than calendar
    /// days, and never negative: a browser clock a little behind the server's says "today", not
    /// "in 2 days".
    /// </summary>
    public static string Ago(DateTimeOffset since, DateTimeOffset now)
    {
        var days = Math.Max(0, (int)(now - since).TotalDays);

        return days switch
        {
            0 => "today",
            1 => "yesterday",
            < 14 => $"{days} days ago",
            < 60 => $"{days / 7} weeks ago",
            < 365 => $"{days / 30} months ago",
            _ => days / 365 == 1 ? "1 year ago" : $"{days / 365} years ago",
        };
    }

    /// <summary>"45 min", "2 h", "1 h 20 min".</summary>
    public static string Duration(int minutes) =>
        minutes < 60 ? $"{minutes} min" : minutes % 60 == 0 ? $"{minutes / 60} h" : $"{minutes / 60} h {minutes % 60} min";
}
