using System.Net;
using System.Net.Http.Json;

namespace ReadingTracker.Library.Tests;

[Collection(LibraryApiCollection.Name)]
public sealed class LibraryEntryTests(LibraryApiFixture fixture)
{
    [Fact]
    public async Task Adds_a_book_to_my_library_and_lists_it()
    {
        var bookId = Guid.NewGuid();
        CatalogKnows(bookId, "The Left Hand of Darkness");
        var client = fixture.ClientFor("reader-adds");

        var created = await client.PostAsJsonAsync("/api/library", new { bookId });

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var entries = await client.GetFromJsonAsync<IReadOnlyList<Entry>>("/api/library");
        var entry = Assert.Single(entries!);
        Assert.Equal(bookId, entry.BookId);
    }

    [Fact]
    public async Task Starts_a_newly_added_book_as_want_to_read()
    {
        var bookId = Guid.NewGuid();
        CatalogKnows(bookId, "A Wizard of Earthsea");
        var client = fixture.ClientFor("reader-defaults");

        var response = await client.PostAsJsonAsync("/api/library", new { bookId });

        var entry = await response.Content.ReadFromJsonAsync<Entry>();
        Assert.Equal("WantToRead", entry!.Status);
        Assert.Equal("Pages", entry.TrackingMethod);
    }

    [Fact]
    public async Task Refuses_to_add_a_book_that_is_already_on_my_shelf()
    {
        var bookId = Guid.NewGuid();
        CatalogKnows(bookId, "The Dispossessed");
        var client = fixture.ClientFor("reader-duplicates");
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/library", new { bookId })).StatusCode);

        var again = await client.PostAsJsonAsync("/api/library", new { bookId });

        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Single((await client.GetFromJsonAsync<IReadOnlyList<Entry>>("/api/library"))!);
    }

    [Fact]
    public async Task Refuses_a_book_the_catalog_has_never_heard_of()
    {
        // The stub's default is "no such book".
        fixture.Catalog.Respond = _ => new HttpResponseMessage(HttpStatusCode.NotFound);

        var response = await fixture.ClientFor("reader-unknown").PostAsJsonAsync("/api/library", new { bookId = Guid.NewGuid() });

        // A clear client error, rather than an entry pointing at nothing.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Shows_me_only_my_own_library()
    {
        var mine = Guid.NewGuid();
        var theirs = Guid.NewGuid();
        CatalogKnows(mine, "My Book");
        await fixture.ClientFor("reader-alice").PostAsJsonAsync("/api/library", new { bookId = mine });
        CatalogKnows(theirs, "Their Book");
        await fixture.ClientFor("reader-bob").PostAsJsonAsync("/api/library", new { bookId = theirs });

        var alices = await fixture.ClientFor("reader-alice").GetFromJsonAsync<IReadOnlyList<Entry>>("/api/library");

        Assert.Equal(mine, Assert.Single(alices!).BookId);
    }

    [Fact]
    public async Task Two_readers_can_each_have_the_same_book()
    {
        var bookId = Guid.NewGuid();
        CatalogKnows(bookId, "Shared Favourite");

        var first = await fixture.ClientFor("reader-carol").PostAsJsonAsync("/api/library", new { bookId });
        var second = await fixture.ClientFor("reader-dave").PostAsJsonAsync("/api/library", new { bookId });

        // One Book, two LibraryEntries: the Book is shared, the relationship is not.
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
    }

    [Fact]
    public async Task Refuses_a_request_that_does_not_say_who_is_asking()
    {
        var response = await fixture.CreateClient().GetAsync("/api/library");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private void CatalogKnows(Guid bookId, string title) =>
        fixture.Catalog.Respond = _ => StubHttpMessageHandler.Json($$"""
            {
              "id": "{{bookId}}",
              "title": "{{title}}",
              "authors": ["Ursula K. Le Guin"],
              "isbn": "9780441478125",
              "coverUrl": "https://example.test/cover.jpg",
              "totalPages": 304
            }
            """);

    private sealed record Entry(Guid Id, Guid BookId, string Status, string TrackingMethod);
}
