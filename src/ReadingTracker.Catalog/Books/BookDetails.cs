using System.Text.RegularExpressions;

namespace ReadingTracker.Catalog.Books;

/// <summary>
/// The longer part of a Book's metadata, as a provider described it: what the book is about
/// and who put it out. Kept apart from the rest because it arrives apart — a search may or
/// may not carry it, and a Book that came without it is asked about once, later, from its
/// own page.
/// </summary>
/// <param name="Description">Plain text, paragraphs separated by a blank line, the provider's markup gone.</param>
/// <param name="PublishedDate">As the provider gives it — a year, a year and month, or a full date.</param>
/// <param name="Categories">At most <see cref="MaxCategories"/>, cleaned, in the provider's order.</param>
public sealed record BookDetails(
    string? Description,
    string? Publisher,
    string? PublishedDate,
    IReadOnlyList<string> Categories)
{
    /// <summary>
    /// Enough to place a book; more than this is a provider's whole taxonomy, not a description
    /// of one title.
    /// </summary>
    public const int MaxCategories = 8;

    public static BookDetails None { get; } = new(null, null, null, []);

    /// <summary>Provider fields as they came, made presentable.</summary>
    public static BookDetails Cleaned(
        string? description,
        string? publisher,
        string? publishedDate,
        IEnumerable<string?>? categories) =>
        new(
            DescriptionText.Clean(description),
            Blank(publisher) ? null : publisher!.Trim(),
            Blank(publishedDate) ? null : publishedDate!.Trim(),
            CleanCategories(categories));

    /// <summary>
    /// Google lists categories as paths — "Fiction / Science Fiction / Space Opera" — and
    /// Open Library as a flat list of subjects, which may have commas of their own ("Earthsea
    /// (Le Guin, Ursula K.), Bk. 1"), so only the path separator splits. Each becomes single
    /// names, trimmed, with repeats dropped whatever their case, Google's "General" left out
    /// because it places nothing, and at most <see cref="MaxCategories"/> kept, first come
    /// first served.
    /// </summary>
    public static IReadOnlyList<string> CleanCategories(IEnumerable<string?>? categories)
    {
        if (categories is null)
        {
            return [];
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var kept = new List<string>(MaxCategories);

        foreach (var name in categories.OfType<string>().SelectMany(category => category.Split(Separators)))
        {
            var trimmed = InnerSpace.Replace(name, " ").Trim();

            if (trimmed.Length == 0 || trimmed.Equals("General", StringComparison.OrdinalIgnoreCase) || !seen.Add(trimmed))
            {
                continue;
            }

            kept.Add(trimmed);

            if (kept.Count == MaxCategories)
            {
                break;
            }
        }

        return kept;
    }

    private static bool Blank(string? value) => string.IsNullOrWhiteSpace(value);

    private static readonly char[] Separators = ['/'];

    private static readonly Regex InnerSpace = new(@"\s+", RegexOptions.Compiled);
}
