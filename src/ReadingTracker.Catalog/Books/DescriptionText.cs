using System.Net;
using System.Text.RegularExpressions;

namespace ReadingTracker.Catalog.Books;

/// <summary>
/// A provider's description made into what a Book stores: plain text, paragraphs separated by a
/// blank line. What providers send is the publisher's jacket copy after a trip through somebody's
/// feed, and it arrives carrying whatever that trip left on it — HTML from Google Books, Markdown
/// from whoever typed it, rules of underscores where a printed flap had a line, a retailer's tag,
/// a library catalogue's credit, a notice about pre-orders or DRM. Each pass below takes off one of
/// those, and each is narrow on purpose: a pass that guessed would sooner or later eat a sentence
/// of the book's own.
///
/// Clean text comes out as it went in, so a description already stored can be put through again
/// whenever a pass is added, and is: see BookEndpoints.
/// </summary>
public static class DescriptionText
{
    public static string? Clean(string? description)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            return null;
        }

        // The whole text first: markup, and the rules that stand between sections, all become
        // paragraph breaks or nothing, since what counts as a paragraph depends on them.
        var text = LineBreaks.Replace(description, "\n");
        text = BlockTags.Replace(text, "\n\n");
        text = OtherTags.Replace(text, string.Empty);
        text = WebUtility.HtmlDecode(text);
        text = Rules.Replace(text, "\n\n");

        var paragraphs = ParagraphGap.Split(text)
            .Select(CleanParagraph)
            .Where(paragraph => paragraph.Length > 0 && !Notices.Any(notice => notice.IsMatch(paragraph)));

        var joined = string.Join("\n\n", paragraphs);

        return joined.Length == 0 ? null : joined;
    }

    /// <summary>Then each paragraph on its own: one line of text, the leftovers taken off it.</summary>
    private static string CleanParagraph(string paragraph)
    {
        var text = InnerSpace.Replace(paragraph, " ").Trim();

        text = Strong.Replace(text, "$2");
        text = Emphasis.Replace(text, "$2");
        text = RetailerTag.Replace(text, string.Empty);
        text = SpacedEllipsis.Replace(text, "…");
        text = SpaceBeforeStop.Replace(text, string.Empty);
        text = RunTogether.Replace(text, " ");
        text = WithoutAttribution(text);

        return text.Trim();
    }

    /// <summary>
    /// A library catalogue credits the publisher for the blurb it quotes — "…series"--Provided by
    /// publisher. The credit goes, and so do the quotation marks it put round the whole of the
    /// blurb, which were only ever there to say it was quoting; the full stop the catalogue put
    /// after the credit is the blurb's own, and stays with it.
    /// </summary>
    private static string WithoutAttribution(string paragraph)
    {
        var match = Attribution.Match(paragraph);

        if (!match.Success)
        {
            return paragraph;
        }

        var quoted = paragraph[..match.Index].Trim();

        var blurb = quoted is ['"' or '“', .. var inner, '"' or '”'] && !inner.Any(IsDoubleQuote)
            ? inner.Trim()
            : quoted;

        return blurb is [.., not ('.' or '!' or '?' or '…')] ? blurb + "." : blurb;
    }

    private static bool IsDoubleQuote(char c) => c is '"' or '“' or '”';

    private const RegexOptions Options = RegexOptions.Compiled | RegexOptions.CultureInvariant;

    // Markup ---------------------------------------------------------------------------------

    private static readonly Regex LineBreaks = new(@"<\s*br\s*/?\s*>|\r?\n", Options | RegexOptions.IgnoreCase);

    private static readonly Regex BlockTags = new(@"</?\s*(p|div|ul|ol|li|h[1-6]|blockquote)\b[^>]*>", Options | RegexOptions.IgnoreCase);

    private static readonly Regex OtherTags = new(@"<[^>]+>", Options);

    private static readonly Regex ParagraphGap = new(@"\n[ \t]*\n", Options);

    private static readonly Regex InnerSpace = new(@"\s+", Options);

    /// <summary>
    /// A rule between sections — "______", "* * *", "-----" — three or more of one mark standing
    /// on its own between spaces, which is where a printed flap had a line. Two dashes are left
    /// alone: that is a dash typed without a dash key, and it is punctuation.
    /// </summary>
    private static readonly Regex Rules = new(@"(?<=^|\s)([_=~*\-–—•])(?:[ \t]*\1){2,}(?=\s|$)", Options | RegexOptions.Multiline);

    /// <summary>Markdown's **strong** and __strong__.</summary>
    private static readonly Regex Strong = new(@"(\*\*|__)(?=\S)(.+?)(?<=\S)\1", Options);

    /// <summary>
    /// Markdown's *emphasis* and _emphasis_ — but only as a pair round words, so snake_case and
    /// "5 * 3" are not taken for it.
    /// </summary>
    private static readonly Regex Emphasis = new(@"(?<![\w*])([*_])(?=[^\s*_])([^*_\n]+?)(?<=[^\s*_])\1(?![\w*])", Options);

    // Leftovers ------------------------------------------------------------------------------

    /// <summary>
    /// A feed's name in brackets stuck to the last word of a blurb it cut short — "push[Bokinfo]."
    /// Stuck on is the tell: a bracket of the author's own has a space before it.
    /// </summary>
    private static readonly Regex RetailerTag = new(@"(?<=\w)\[[A-Za-z]+\]", Options);

    /// <summary>". . ." set the way a typewriter would, made the one character it stands for.</summary>
    private static readonly Regex SpacedEllipsis = new(@"\.(?: \.){2}", Options);

    /// <summary>
    /// "Osskil ." — a space before a full stop or a comma. Not before ; : ! or ?, which French
    /// sets with a space and a French description should keep; and not before a run of dots.
    /// </summary>
    private static readonly Regex SpaceBeforeStop = new(@"(?<=\w) +(?=[.,](?!\.))", Options);

    /// <summary>
    /// "out of time.These new editions" — the space after a sentence lost where two paragraphs
    /// were glued. Only after a word of two letters or more and before a capitalised word, so
    /// initials (J.K.), domains (Amazon.com) and decimals (3.14) are not split.
    /// </summary>
    private static readonly Regex RunTogether = new(@"(?<=[a-z]{2}[.!?])(?=[A-Z][a-z])", Options);

    /// <summary>A library catalogue's credit for the blurb, at the end of it.</summary>
    private static readonly Regex Attribution = new(
        @"\s*-{1,2}\s*(?:Provided by (?:the )?publisher|Publisher['’]?s? description)\.?$",
        Options | RegexOptions.IgnoreCase);

    // Notices --------------------------------------------------------------------------------

    /// <summary>
    /// Paragraphs that are about the listing rather than the book, dropped whole. Whole
    /// paragraphs only, and only these: the same words inside a paragraph of the story are the
    /// story's.
    /// </summary>
    private static readonly Regex[] Notices =
    [
        new(@"^pre-?order\b", Options | RegexOptions.IgnoreCase),
        new(@"^At the publisher['’]s request, this title is being sold without Digital Rights Management", Options | RegexOptions.IgnoreCase),
    ];
}
