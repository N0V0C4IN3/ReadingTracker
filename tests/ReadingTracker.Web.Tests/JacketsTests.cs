using ReadingTracker.Web.Components;

namespace ReadingTracker.Web.Tests;

public class JacketsTests
{
    [Fact]
    public void Opens_on_one_of_the_wall()
    {
        var front = Jackets.PickFront(new Random(7));

        Assert.InRange(front, 0, Jackets.Wall.Length - 1);
    }

    [Fact]
    public void Opens_on_a_different_book_from_one_visit_to_the_next()
    {
        var seen = Enumerable.Range(0, 200).Select(seed => Jackets.PickFront(new Random(seed))).Distinct().Count();

        Assert.True(seen > 1, "two hundred visits opened on the same book every time");
    }

    [Fact]
    public void Can_open_on_any_book_of_the_wall()
    {
        var seen = Enumerable.Range(0, 2000).Select(seed => Jackets.PickFront(new Random(seed))).Distinct().Count();

        Assert.Equal(Jackets.Wall.Length, seen);
    }
}
