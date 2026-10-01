namespace ReadingTracker.Web.Services;

/// <summary>
/// How much of a book a reader has read, as one of four cases a display can tell apart, rather
/// than the three fields of <see cref="Progress"/>, which mean something only together:
/// <c>AmountRead</c> and <c>Unit</c> are null together, and <c>PercentComplete</c> is null when
/// the length of the book is not known.
///
/// Library works the total out (adding sessions up, converting between units, stopping at the
/// end of the book); this only reads what it said, once, so no display re-derives it. A shape
/// Library cannot produce is not trusted: it lands on <see cref="NeedsPageCount"/>, which says
/// nothing about how far the reader is, rather than guessing or failing.
/// </summary>
public abstract record AmountRead
{
    private AmountRead()
    {
    }

    /// <summary>
    /// How far through the book, as a whole percent, where that can be said; null where it cannot.
    /// A whole percent never rounds up to 100, so 100 is the whole book and nothing less.
    /// </summary>
    public abstract int? PercentComplete { get; }

    /// <summary>
    /// Read to the end. Offered, never done for the reader: a log that gets there is a good moment
    /// to ask whether they have finished, and no more.
    /// </summary>
    public bool ReachedTheEnd => PercentComplete == 100;

    /// <summary>What has been read in pages, when the total is said in pages; null in percent, or not at all.</summary>
    public decimal? PagesRead => this is InPages pages ? pages.Amount : null;

    /// <summary>
    /// Reads Library's answer. Null is nothing logged, which is not the same as none of it read.
    /// </summary>
    public static AmountRead Of(Progress? progress) => progress switch
    {
        null => new NotStarted(),
        { AmountRead: { } amount, Unit: "Percentage", PercentComplete: { } whole } => new InPercent(amount, whole),
        { AmountRead: { } amount, Unit: "Pages" } => new InPages(amount, progress.PercentComplete),
        _ => new NeedsPageCount(),
    };

    /// <summary>Nothing has been logged, and no device has said anything: not zero, none at all.</summary>
    public sealed record NotStarted : AmountRead
    {
        public override int? PercentComplete => null;
    }

    /// <summary>
    /// Some of it logged in pages and some in percent, and no page count to add the two together
    /// with. The reader can fix it by saying how long their edition is.
    /// </summary>
    public sealed record NeedsPageCount : AmountRead
    {
        public override int? PercentComplete => null;
    }

    /// <summary>The total is said in percent, which is how the reader tracks this book: <paramref name="Amount"/> as they would say it, and its whole percent.</summary>
    public sealed record InPercent(decimal Amount, int Whole) : AmountRead
    {
        public override int? PercentComplete => Whole;
    }

    /// <summary>
    /// The total is said in pages. <paramref name="Whole"/> is its whole percent, null when nobody
    /// knows how long the book is, so there is nothing to be a share of.
    /// </summary>
    public sealed record InPages(decimal Amount, int? Whole) : AmountRead
    {
        public override int? PercentComplete => Whole;
    }
}
