using System.Net;
using System.Net.Http.Json;

namespace ReadingTracker.Library.Tests;

[Collection(LibraryApiCollection.Name)]
public sealed class RemoveFromLibraryTests(LibraryApiFixture fixture)
{
    [Fact]
    public async Task Takes_a_book_off_my_shelf()
    {
        var client = fixture.ClientFor("removing-reader");
        var entryId = await fixture.AddBookAsync(client, 300);

        var response = await client.DeleteAsync($"/api/library/{entryId}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Empty((await client.GetFromJsonAsync<IReadOnlyList<Entry>>("/api/library"))!);
    }

    [Fact]
    public async Task Takes_the_reading_history_with_it_and_lets_me_start_the_book_again()
    {
        var bookId = Guid.NewGuid();
        fixture.CatalogHasBook(bookId, 300);
        var client = fixture.ClientFor("restarting-reader");
        var entryId = await AddAsync(client, bookId);
        await LogAsync(client, entryId, 1, 150);
        await client.PutAsJsonAsync($"/api/library/{entryId}/status", new { status = "OnHold" });

        await client.DeleteAsync($"/api/library/{entryId}");
        var again = await AddAsync(client, bookId);

        // The same Book, added back, with nothing carried over from the entry that went.
        var entry = (await client.GetFromJsonAsync<IReadOnlyList<Entry>>("/api/library"))!.Single();
        Assert.Equal(again, entry.Id);
        Assert.NotEqual(entryId, entry.Id);
        Assert.Equal("WantToRead", entry.Status);
        Assert.Null(entry.Progress);
        Assert.Empty((await client.GetFromJsonAsync<IReadOnlyList<Session>>($"/api/library/{again}/sessions"))!);
    }

    [Fact]
    public async Task Leaves_the_sessions_nowhere_to_be_found()
    {
        var client = fixture.ClientFor("orphan-check-reader");
        var entryId = await fixture.AddBookAsync(client, 300);
        await LogAsync(client, entryId, 1, 150);

        await client.DeleteAsync($"/api/library/{entryId}");

        // Nothing left pointing at an entry that no longer exists.
        var sessions = await client.GetAsync($"/api/library/{entryId}/sessions");
        Assert.Equal(HttpStatusCode.NotFound, sessions.StatusCode);
    }

    [Fact]
    public async Task Leaves_the_book_itself_alone_for_every_other_reader()
    {
        var bookId = Guid.NewGuid();
        fixture.CatalogHasBook(bookId, 300);
        var mine = fixture.ClientFor("quitting-reader");
        var theirs = fixture.ClientFor("continuing-reader");
        var myEntry = await AddAsync(mine, bookId);
        await AddAsync(theirs, bookId);
        fixture.Catalog.Requests.Clear();

        await mine.DeleteAsync($"/api/library/{myEntry}");

        // Only this reader's entry goes: the Book is shared, and Library never writes to Catalog.
        Assert.Equal(bookId, Assert.Single((await theirs.GetFromJsonAsync<IReadOnlyList<Entry>>("/api/library"))!).BookId);
        Assert.All(fixture.Catalog.Requests, request => Assert.Equal(HttpMethod.Get, request.Method));
    }

    [Fact]
    public async Task Tells_me_plainly_when_there_is_nothing_to_remove()
    {
        var response = await fixture.ClientFor("empty-shelf-reader").DeleteAsync($"/api/library/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Refuses_to_take_a_book_off_someone_elses_shelf()
    {
        var owner = fixture.ClientFor("shelf-owner");
        var entryId = await fixture.AddBookAsync(owner, 300);

        var response = await fixture.ClientFor("shelf-intruder").DeleteAsync($"/api/library/{entryId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Single((await owner.GetFromJsonAsync<IReadOnlyList<Entry>>("/api/library"))!);
    }

    private static Task<HttpResponseMessage> LogAsync(HttpClient client, Guid entryId, decimal start, decimal end) =>
        client.PostAsJsonAsync($"/api/library/{entryId}/sessions", new
        {
            startPosition = start,
            endPosition = end,
        });

    private static async Task<Guid> AddAsync(HttpClient client, Guid bookId) =>
        (await (await client.PostAsJsonAsync("/api/library", new { bookId })).Content.ReadFromJsonAsync<Entry>())!.Id;

    private sealed record Entry(Guid Id, Guid BookId, string Status, Progress? Progress);

    private sealed record Progress(decimal Position, string Unit, int? PercentComplete);

    private sealed record Session(Guid Id);
}
