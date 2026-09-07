using System.Net;
using System.Net.Http.Json;

namespace ReadingTracker.Catalog.Tests;

[Collection(CatalogApiCollection.Name)]
public sealed class BatchBookLookupTests(CatalogApiFixture fixture)
{
    [Fact]
    public async Task Returns_every_book_asked_for_in_one_request()
    {
        var client = fixture.CreateClient();
        var first = await AddBookAsync(client, "Batch One", "9784444444442");
        var second = await AddBookAsync(client, "Batch Two", "9785555555559");

        var books = await client.GetFromJsonAsync<IReadOnlyList<Book>>(
            $"/api/books?ids={first}&ids={second}");

        Assert.Equal(2, books!.Count);
        Assert.Contains(books, book => book.Id == first);
        Assert.Contains(books, book => book.Id == second);
    }

    [Fact]
    public async Task Leaves_out_ids_that_match_no_book_rather_than_failing()
    {
        var client = fixture.CreateClient();
        var known = await AddBookAsync(client, "Batch Three", "9786666666666");
        var unknown = Guid.NewGuid();

        var response = await client.GetAsync($"/api/books?ids={known}&ids={unknown}");

        // A caller rendering a list shouldn't lose every book because one id went stale.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var books = await response.Content.ReadFromJsonAsync<IReadOnlyList<Book>>();
        Assert.Equal(known, Assert.Single(books!).Id);
    }

    [Fact]
    public async Task Rejects_a_request_that_asks_for_nothing()
    {
        var response = await fixture.CreateClient().GetAsync("/api/books");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Rejects_a_request_that_asks_for_an_unreasonable_number_of_books()
    {
        var tooMany = string.Join("&", Enumerable.Range(0, 201).Select(_ => $"ids={Guid.NewGuid()}"));

        var response = await fixture.CreateClient().GetAsync($"/api/books?{tooMany}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static async Task<Guid> AddBookAsync(HttpClient client, string title, string isbn)
    {
        var response = await client.PostAsJsonAsync("/api/books", new
        {
            title,
            authors = new[] { "A. Writer" },
            isbn,
        });

        return (await response.Content.ReadFromJsonAsync<Book>())!.Id;
    }

    private sealed record Book(Guid Id, string Title);
}
