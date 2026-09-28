using ReadingTracker.Web.Services;

namespace ReadingTracker.Web.Tests;

public class CoverCssTests
{
    [Fact]
    public void Quotes_a_plain_address()
    {
        Assert.Equal("url(\"https://covers.example/1-M.jpg\")", CoverCss.Url("https://covers.example/1-M.jpg"));
    }

    [Fact]
    public void Encodes_what_could_end_the_string()
    {
        Assert.Equal(
            "url(\"https://x.example/a%22b%5Cc%0Ad%0De\")",
            CoverCss.Url("https://x.example/a\"b\\c\nd\re"));
    }

    [Fact]
    public void Leaves_single_quotes_and_parentheses_inside_the_quotes()
    {
        Assert.Equal("url(\"https://x.example/it's(1).jpg\")", CoverCss.Url("https://x.example/it's(1).jpg"));
    }
}
