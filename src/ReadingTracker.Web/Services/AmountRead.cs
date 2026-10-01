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
    private static readonly NeedsPageCount Unbridged = new();

    private AmountRead(int? percentComplete) => PercentComplete = percentComplete;

    /// <summary>Nothing logged: the case for no progress at all, and what a display starts from.</summary>
    public static AmountRead None { get; } = new NotStarted();

    /// <summary>
    /// How far through the book, as a whole percent, where that can be said; null where it cannot.
    /// A whole percent never rounds up to 100, so 100 is the whole book and nothing less.
    /// </summary>
    public int? PercentComplete { get; }

    /// <summary>
    /// Read to the end. Offered, never done for the reader: a log that gets there is a good moment
    /// to ask whether they have finished, and no more.
    /// </summary>
    public bool ReachedTheEnd => PercentComplete == 100;

    /// <summary>What has been read in pages, when the total is said in pages; null in percent, or not at all.</summary>
    public virtual decimal? PagesRead => null;

    /// <summary>
    /// Reads Library's answer. Null is nothing logged, which is not the same as none of it read.
    /// </summary>
    public static AmountRead Of(Progress? progress) => progress switch
    {
        null => None,
        { AmountRead: not null, Unit: "Percentage", PercentComplete: { } whole } => new InPercent(whole),
        { AmountRead: { } amount, Unit: "Pages" } => new InPages(amount, progress.PercentComplete),
        _ => Unbridged,
    };

    /// <summary>Nothing has been logged, and no device has said anything: not zero, none at all.</summary>
    public sealed record NotStarted : AmountRead
    {
        public NotStarted()
            : base((int?)null)
        {
        }
    }

    /// <summary>
    /// Some of it logged in pages and some in percent, and no page count to add the two together
    /// with. The reader can fix it by saying how long their edition is.
    /// </summary>
    public sealed record NeedsPageCount : AmountRead
    {
        public NeedsPageCount()
            : base((int?)null)
        {
        }
    }

    /// <summary>The total is said in percent, which is how the reader tracks this book; its whole percent is all a display says.</summary>
    public sealed record InPercent : AmountRead
    {
        public InPercent(int percentComplete)
            : base(percentComplete)
        {
        }
    }

    /// <summary>
    /// The total is said in pages. Its whole percent is null when nobody knows how long the book
    /// is, so there is nothing for it to be a share of.
    /// </summary>
    public sealed record InPages : AmountRead
    {
        public InPages(decimal amount, int? percentComplete)
            : base(percentComplete) => Amount = amount;

        public decimal Amount { get; }

        public override decimal? PagesRead => Amount;
    }
}
