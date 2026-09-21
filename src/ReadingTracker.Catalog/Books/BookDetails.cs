using System.Net;
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
            CleanDescription(description),
            Blank(publisher) ? null : publisher!.Trim(),
            Blank(publishedDate) ? null : publishedDate!.Trim(),
            CleanCategories(categories));

    /// <summary>
    /// Google Books' descriptions are HTML — paragraphs, line breaks, the odd bold run — and
    /// Open Library's are plain text with its own line breaks. Both come out as paragraphs of
    /// plain text: block-level markup and a blank line (two breaks together, of either kind)
    /// end a paragraph, a lone break is a space, every other tag is dropped, entities are
    /// decoded, and whitespace inside a paragraph is one space.
    /// </summary>
    public static string? CleanDescription(string? description)
    {
        if (Blank(description))
        {
            return null;
        }

        var text = LineBreaks.Replace(description!, "\n");
        text = BlockTags.Replace(text, "\n\n");
        text = OtherTags.Replace(text, string.Empty);
        text = WebUtility.HtmlDecode(text);

        var paragraphs = ParagraphGap.Split(text)
            .Select(paragraph => InnerSpace.Replace(paragraph, " ").Trim())
            .Where(paragraph => paragraph.Length > 0);

        var joined = string.Join("\n\n", paragraphs);

        return joined.Length == 0 ? null : joined;
    }

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

    private static readonly Regex LineBreaks = new(@"<\s*br\s*/?\s*>|\r?\n", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex BlockTags = new(
        @"</?\s*(p|div|ul|ol|li|h[1-6]|blockquote)\b[^>]*>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex OtherTags = new(@"<[^>]+>", RegexOptions.Compiled);

    private static readonly Regex ParagraphGap = new(@"\n[ \t]*\n", RegexOptions.Compiled);

    private static readonly Regex InnerSpace = new(@"\s+", RegexOptions.Compiled);
}
