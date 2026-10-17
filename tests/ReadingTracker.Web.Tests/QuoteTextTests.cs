using ReadingTracker.Web.Services;

namespace ReadingTracker.Web.Tests;

/// <summary>What Copy puts on the clipboard: the Book's words and where they are from, never the reader's note.</summary>
public sealed class QuoteTextTests
{
    [Fact]
    public void Copies_the_passage_with_its_author_and_title()
    {
        var text = QuoteText.ForCopy("What do we live for?", ["George Eliot"], "Middlemarch");

        Assert.Equal("“What do we live for?” — George Eliot, Middlemarch", text);
    }

    [Fact]
    public void Names_every_author_the_way_the_byline_does()
    {
        var text = QuoteText.ForCopy("Words.", ["Terry Pratchett", "Neil Gaiman"], "Good Omens");

        Assert.Equal("“Words.” — Terry Pratchett, Neil Gaiman, Good Omens", text);
    }

    [Fact]
    public void Gives_only_the_title_when_nobody_knows_who_wrote_it()
    {
        Assert.Equal("“Words.” — Beowulf", QuoteText.ForCopy("Words.", [], "Beowulf"));
        Assert.Equal("“Words.” — Beowulf", QuoteText.ForCopy("Words.", null, "Beowulf"));
    }

    [Fact]
    public void Gives_only_the_passage_when_the_book_is_not_known_either()
    {
        Assert.Equal("“Words.”", QuoteText.ForCopy("Words.", null, null));
    }

    [Fact]
    public void Trims_the_passage_so_stray_space_from_the_device_does_not_sit_inside_the_quotes()
    {
        Assert.Equal("“Words.” — Beowulf", QuoteText.ForCopy("  Words.\n", null, "Beowulf"));
    }
}
