using ReadingTracker.Web.Pages;

namespace ReadingTracker.Web.Tests;

public class SayingsTests
{
    [Fact]
    public void Every_saying_has_words_and_a_name_to_go_with_them()
    {
        Assert.NotEmpty(Sayings.All);
        Assert.All(Sayings.All, saying =>
        {
            Assert.False(string.IsNullOrWhiteSpace(saying.Text));
            Assert.False(string.IsNullOrWhiteSpace(saying.Author));
        });
    }

    [Fact]
    public void Says_each_thing_once()
    {
        Assert.Equal(Sayings.All.Length, Sayings.All.Select(saying => saying.Text).Distinct().Count());
    }

    [Fact]
    public void Picks_one_of_its_own()
    {
        Assert.Contains(Sayings.Pick(new Random(7)), Sayings.All);
    }

    [Fact]
    public void Picks_differently_from_one_visit_to_the_next()
    {
        var seen = Enumerable.Range(0, 200).Select(seed => Sayings.Pick(new Random(seed))).Distinct().Count();

        Assert.True(seen > 1, "two hundred visits saw the same saying every time");
    }
}
