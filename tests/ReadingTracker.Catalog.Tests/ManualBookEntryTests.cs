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
}
