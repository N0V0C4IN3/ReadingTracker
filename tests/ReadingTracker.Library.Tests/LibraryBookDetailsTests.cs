using System.Net;
using System.Net.Http.Json;

namespace ReadingTracker.Library.Tests;

[Collection(LibraryApiCollection.Name)]
public sealed class LibraryBookDetailsTests(LibraryApiFixture fixture)
{
    [Fact]
    public async Task Shows_the_title_authors_and_cover_for_each_book_on_my_shelf()
    {
        var bookId = Guid.NewGuid();
        var client = fixture.ClientFor("details-reader");
        CatalogHas([(bookId, "The Lathe of Heaven")]);
        await client.PostAsJsonAsync("/api/library", new { bookId });

        var entries = await client.GetFromJsonAsync<IReadOnlyList<Entry>>("/api/library");

        var entry = Assert.Single(entries!);
        Assert.NotNull(entry.Book);
        Assert.Equal("The Lathe of Heaven", entry.Book!.Title);
        Assert.Equal(["Ursula K. Le Guin"], entry.Book.Authors);
        Assert.Equal("https://example.test/cover.jpg", entry.Book.CoverUrl);
        Assert.Equal(304, entry.Book.TotalPages);
    }

    [Fact]
    public async Task Asks_the_catalog_once_for_a_whole_shelf_rather_than_once_per_book()
    {
        var client = fixture.ClientFor("batch-reader");
        var books = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        foreach (var bookId in books)
        {
            CatalogHas([(bookId, $"Book {bookId}")]);
            await client.PostAsJsonAsync("/api/library", new { bookId });
        }

        CatalogHas(books.Select(id => (id, $"Book {id}")).ToArray());
        var callsBefore = fixture.Catalog.RequestCount;
        var entries = await client.GetFromJsonAsync<IReadOnlyList<Entry>>("/api/library");

        Assert.Equal(3, entries!.Count);
        // Three books on the shelf, one trip to Catalog.
        Assert.Equal(1, fixture.Catalog.RequestCount - callsBefore);
    }

    [Fact]
    public async Task Still_gives_me_my_shelf_when_the_catalog_is_down()
    {
        var bookId = Guid.NewGuid();
        var client = fixture.ClientFor("outage-reader");
        CatalogHas([(bookId, "A Book I Own")]);
        await client.PostAsJsonAsync("/api/library", new { bookId });

        fixture.Catalog.Respond = _ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        var response = await client.GetAsync("/api/library");

        // Losing book covers is a degraded shelf; losing the shelf is a broken app.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var entry = Assert.Single((await response.Content.ReadFromJsonAsync<IReadOnlyList<Entry>>())!);
        Assert.Equal(bookId, entry.BookId);
        Assert.Equal("WantToRead", entry.Status);
        Assert.Null(entry.Book);
    }

    /// <summary>
    /// Answers both the single-book lookup used when adding and the batch lookup used when listing.
    /// </summary>
    private void CatalogHas(params (Guid Id, string Title)[] books) =>
        fixture.Catalog.Respond = request =>
        {
            var single = books.FirstOrDefault(book =>
                request.RequestUri!.AbsolutePath.EndsWith(book.Id.ToString(), StringComparison.OrdinalIgnoreCase));

            return StubHttpMessageHandler.Json(
                single.Id == Guid.Empty
                    ? $"[{string.Join(",", books.Select(book => BookJson(book.Id, book.Title)))}]"
                    : BookJson(single.Id, single.Title));
        };

    private static string BookJson(Guid id, string title) => $$"""
        {
          "id": "{{id}}",
          "title": "{{title}}",
          "authors": ["Ursula K. Le Guin"],
          "isbn": "9780380014422",
          "coverUrl": "https://example.test/cover.jpg",
          "totalPages": 304
        }
        """;

    private sealed record Entry(Guid Id, Guid BookId, string Status, BookDetails? Book);

    private sealed record BookDetails(string Title, IReadOnlyList<string> Authors, string? CoverUrl, int? TotalPages);
}
