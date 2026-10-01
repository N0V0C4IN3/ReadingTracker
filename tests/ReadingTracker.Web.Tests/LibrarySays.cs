using ReadingTracker.Web.Services;

namespace ReadingTracker.Web.Tests;

/// <summary>
/// The shapes of <see cref="Progress"/> Library really produces, and no others. This application
/// cannot reference Library, so a test has to build what Library would have said by hand, and a
/// hand-built <c>Progress</c> can be a combination no real answer ever is — a percent with no
/// amount, say — and a test of that proves nothing about the page a reader sees. Build from here;
/// the shapes Library cannot produce are written out longhand, in the one place that says what
/// is done with them (<c>AmountReadTests</c>).
/// </summary>
internal static class LibrarySays
{
    /// <summary>Nothing logged, and no device has said anything: Library sends no progress at all.</summary>
    public static Progress? Nothing => null;

    /// <summary>Pages and percent both logged, and no page count to add them with.</summary>
    public static Progress CannotTotal => new(null, null, null);

    /// <summary>
    /// A total in pages. <paramref name="percentComplete"/> is its whole percent, null exactly
    /// when nobody knows how long the book is.
    /// </summary>
    public static Progress Pages(decimal amount, int? percentComplete) => new(amount, "Pages", percentComplete);

    /// <summary>A total in percent, which always has its whole percent: it is the unit.</summary>
    public static Progress Percent(decimal amount, int percentComplete) => new(amount, "Percentage", percentComplete);
}
