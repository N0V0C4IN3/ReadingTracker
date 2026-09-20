using ReadingTracker.Web.Pages;

namespace ReadingTracker.Web.Tests;

public class QuotesTests
{
    [Fact]
    public void Every_quote_has_words_and_a_name_to_go_with_them()
    {
        Assert.NotEmpty(Quotes.All);
        Assert.All(Quotes.All, quote =>
        {
            Assert.False(string.IsNullOrWhiteSpace(quote.Text));
            Assert.False(string.IsNullOrWhiteSpace(quote.Author));
        });
    }

    [Fact]
    public void Says_each_thing_once()
    {
        Assert.Equal(Quotes.All.Length, Quotes.All.Select(quote => quote.Text).Distinct().Count());
    }

    [Fact]
    public void Picks_one_of_its_own()
    {
        Assert.Contains(Quotes.Pick(new Random(7)), Quotes.All);
    }

    [Fact]
    public void Picks_differently_from_one_visit_to_the_next()
    {
        var seen = Enumerable.Range(0, 200).Select(seed => Quotes.Pick(new Random(seed))).Distinct().Count();

        Assert.True(seen > 1, "two hundred visits saw the same quote every time");
    }
}
