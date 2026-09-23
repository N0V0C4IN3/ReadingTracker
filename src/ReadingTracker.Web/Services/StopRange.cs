namespace ReadingTracker.Web.Services;

/// <summary>
/// The stretch of a book a new ReadingSession can cover, for saying how much was read by where
/// the reader stopped rather than by a number: from what is read already to the end, in the
/// book's TrackingMethod — pages of its page count, or percent of 100.
///
/// What is sent is still an amount (ADR-0010): where they stopped less where they were. The
/// stop is only how the reader says it, because "I got to page 180" is what a reader knows and
/// "I read 42 pages" is arithmetic they would otherwise do in their head.
/// </summary>
public sealed record StopRange(bool Pages, int Total, int Read)
{
    /// <summary>
    /// The range for a book, or null when there is none to slide along: a book tracked in pages
    /// with no page count has no end, and a book read to its end has nowhere left to stop.
    /// </summary>
    public static StopRange? For(string trackingMethod, int? pageCount, Progress? progress)
    {
        var pages = trackingMethod != "Percentage";

        if (pages && pageCount is not > 0)
        {
            return null;
        }

        var total = pages ? pageCount!.Value : 100;

        // Library adds the sessions up in the book's unit, or leaves the amount out when they were
        // logged in both units and there is no page count to add them with; the percent is there
        // either way, whenever a page count is.
        var read = pages
            ? progress is { Unit: "Pages", AmountRead: { } amount } ? amount
              : progress?.PercentComplete is { } percent ? percent * total / 100m : 0
            : progress?.PercentComplete ?? 0;

        var at = (int)Math.Clamp(Math.Floor(read), 0, total);

        return at >= total ? null : new StopRange(pages, total, at);
    }

    /// <summary>The most one session can say: the rest of the book.</summary>
    public int Left => Total - Read;

    /// <summary>A stop is never behind where the reader already was, nor past the end.</summary>
    public int Clamp(int stop) => Math.Clamp(stop, Read, Total);

    public int AmountTo(int stop) => Clamp(stop) - Read;

    public int StopFor(decimal? amount) => Clamp(Read + (int)Math.Floor(amount ?? 0));

    /// <summary>"Page 180" or "68%".</summary>
    public string Place(int stop) => Pages ? $"Page {Clamp(stop)}" : $"{Clamp(stop)}%";

    /// <summary>"+42 pages", "+1 page", "+12%".</summary>
    public string Gain(int stop) => AmountTo(stop) switch
    {
        var amount when !Pages => $"+{amount}%",
        1 => "+1 page",
        var amount => $"+{amount} pages",
    };
}
