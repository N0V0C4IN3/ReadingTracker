using System.Net;
using System.Net.Http.Json;

namespace ReadingTracker.Catalog.Tests;

[Collection(CatalogApiCollection.Name)]
public sealed class ManualBookEntryTests(CatalogApiFixture fixture)
{
    [Fact]
    public async Task Creates_a_book_from_details_typed_in_by_hand()
    {
        var client = fixture.CreateClient();

        var response = await client.PostAsJsonAsync("/api/books", new
        {
            title = "An Unpublished Manuscript",
            authors = new[] { "A. Writer" },
            isbn = "9781111111117",
            coverUrl = "https://example.test/cover.jpg",
            totalPages = 210,
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<Book>();
        Assert.Equal("Manual", created!.Source);

        // The whole point of storing it is being able to read it back later.
        var fetched = await client.GetFromJsonAsync<Book>($"/api/books/{created.Id}");
        Assert.Equal(created.Id, fetched!.Id);
        Assert.Equal("An Unpublished Manuscript", fetched.Title);
        Assert.Equal(["A. Writer"], fetched.Authors);
        Assert.Equal(210, fetched.TotalPages);
    }

    [Fact]
    public async Task Accepts_a_book_with_only_the_details_the_reader_actually_has()
    {
        var response = await fixture.CreateClient().PostAsJsonAsync("/api/books", new
        {
            title = "A Book With No ISBN",
            authors = new[] { "Someone Self-Published" },
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<Book>();
        Assert.Null(created!.Isbn);
        Assert.Null(created.TotalPages);
    }

    [Theory]
    [InlineData(null, new[] { "A. Writer" })]
    [InlineData("   ", new[] { "A. Writer" })]
    [InlineData("A Title", null)]
    [InlineData("A Title", new string[0])]
    public async Task Rejects_a_book_that_is_missing_a_title_or_an_author(string? title, string[]? authors)
    {
        var response = await fixture.CreateClient().PostAsJsonAsync("/api/books", new { title, authors });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    /// Anyone signed in can add a Book, and a Book with an ISBN is shown to every other reader
    /// who searches for it. So what a Book can be made of is bounded, and a refusal names the
    /// field so the form can show it there.
    /// </summary>
    [Theory]
    [MemberData(nameof(OverTheLine))]
    public async Task Refuses_details_it_cannot_honour_and_names_the_field(string field, object book)
    {
        var response = await fixture.CreateClient().PostAsJsonAsync("/api/books", book);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblem>();
        Assert.Contains(field, problem!.Errors!.Keys);
    }

    public static TheoryData<string, object> OverTheLine() => new()
    {
        { "Title", new { title = new string('t', 201), authors = new[] { "A. Writer" } } },
        { "Authors", new { title = "Long Author", authors = new[] { new string('a', 201) } } },
        { "Authors", new { title = "Too Many Authors", authors = Enumerable.Range(1, 11).Select(n => $"Author {n}").ToArray() } },
        { "Isbn", new { title = "Not An ISBN", authors = new[] { "A. Writer" }, isbn = "12345" } },
        { "CoverUrl", new { title = "Plain HTTP Cover", authors = new[] { "A. Writer" }, coverUrl = "http://example.test/cover.jpg" } },
        { "CoverUrl", new { title = "Relative Cover", authors = new[] { "A. Writer" }, coverUrl = "/covers/cover.jpg" } },
        { "CoverUrl", new { title = "Endless Cover", authors = new[] { "A. Writer" }, coverUrl = "https://example.test/" + new string('c', 2000) } },
        { "TotalPages", new { title = "Absurdly Long", authors = new[] { "A. Writer" }, totalPages = 20_001 } },
    };

    [Fact]
    public async Task Accepts_details_that_sit_exactly_on_the_line()
    {
        var response = await fixture.CreateClient().PostAsJsonAsync("/api/books", new
        {
            title = new string('t', 200),
            authors = Enumerable.Range(1, 10).Select(n => new string((char)('a' + n), 200)).ToArray(),
            isbn = "978-3-16-148410-0",
            coverUrl = "https://example.test/" + new string('c', 2000 - "https://example.test/".Length),
            totalPages = 20_000,
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<Book>();
        // Stored in the shape an ISBN search looks it up by, hyphens gone.
        Assert.Equal("9783161484100", created!.Isbn);
    }

    [Fact]
    public async Task Refuses_to_create_a_second_book_for_an_isbn_that_is_already_known()
    {
        var client = fixture.CreateClient();
        var book = new
        {
            title = "First One In",
            authors = new[] { "A. Writer" },
            isbn = "9782222222226",
        };
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/books", book)).StatusCode);

        var duplicate = await client.PostAsJsonAsync("/api/books", book);

        // One Book per ISBN, so this is a conflict rather than a server error.
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
    }

    private sealed record Book(
        Guid Id,
        string Title,
        IReadOnlyList<string> Authors,
        string? Isbn,
        string? CoverUrl,
        int? TotalPages,
        string Source);

    private sealed record ValidationProblem(Dictionary<string, string[]>? Errors);
}
