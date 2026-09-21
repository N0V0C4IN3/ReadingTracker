using System.Net;
using System.Net.Http.Json;

namespace ReadingTracker.Library.Tests;

[Collection(LibraryApiCollection.Name)]
public sealed class ChangeBookTests(LibraryApiFixture fixture)
{
    [Fact]
    public async Task Points_my_entry_at_the_edition_i_am_actually_reading_and_keeps_what_i_have_done()
    {
        var client = fixture.ClientFor("wrong-edition-reader");
        var entryId = await fixture.AddBookAsync(client, 300);
        await LogAsync(client, entryId, 120);
        await client.PutAsJsonAsync($"/api/library/{entryId}/status", new { status = "Reading" });
        var rightEdition = Guid.NewGuid();
        fixture.CatalogHasBook(rightEdition, 480);

        var response = await ChangeBookAsync(client, entryId, rightEdition);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var entry = await EntryAsync(client, entryId);
        Assert.Equal(rightEdition, entry.BookId);
        Assert.Equal("Reading", entry.Status);
        Assert.Equal(480, entry.EffectivePageCount);
        // The 120 pages stand, now against the longer book.
        Assert.Equal(120, entry.Progress!.AmountRead);
        Assert.Equal(25, entry.Progress.PercentComplete);
        Assert.Single(await client.GetFromJsonAsync<IReadOnlyList<Session>>($"/api/library/{entryId}/sessions") ?? []);
    }

    [Fact]
    public async Task Drops_the_page_count_i_set_for_the_edition_i_no_longer_have()
    {
        var client = fixture.ClientFor("recounted-edition-reader");
        var entryId = await fixture.AddBookAsync(client, 300);
        await client.PutAsJsonAsync($"/api/library/{entryId}/page-count", new { totalPages = 705 });
        var other = Guid.NewGuid();
        fixture.CatalogHasBook(other, 480);

        await ChangeBookAsync(client, entryId, other);

        var entry = await EntryAsync(client, entryId);
        Assert.Null(entry.PageCountOverride);
        Assert.Equal(480, entry.EffectivePageCount);
    }

    [Fact]
    public async Task Refuses_an_edition_the_catalog_has_never_heard_of()
    {
        var client = fixture.ClientFor("phantom-edition-reader");
        var entryId = await fixture.AddBookAsync(client, 300);
        var before = (await EntryAsync(client, entryId)).BookId;
        fixture.Catalog.Respond = _ => new HttpResponseMessage(HttpStatusCode.NotFound);

        var response = await ChangeBookAsync(client, entryId, Guid.NewGuid());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        fixture.CatalogHasBook(before, 300);
        Assert.Equal(before, (await EntryAsync(client, entryId)).BookId);
    }

    [Fact]
    public async Task Refuses_an_edition_that_is_already_its_own_entry_on_my_shelf()
    {
        var client = fixture.ClientFor("doubled-edition-reader");
        var first = await fixture.AddBookAsync(client, 300);
        var secondBook = Guid.NewGuid();
        fixture.CatalogHasBook(secondBook, 320);
        await client.PostAsJsonAsync("/api/library", new { bookId = secondBook });

        var response = await ChangeBookAsync(client, first, secondBook);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblem>();
        Assert.Contains("already on your shelf", problem!.Errors!["BookId"].Single());
    }

    [Fact]
    public async Task Refuses_to_change_the_edition_on_someone_elses_shelf()
    {
        var entryId = await fixture.AddBookAsync(fixture.ClientFor("edition-owner"), 300);
        var other = Guid.NewGuid();
        fixture.CatalogHasBook(other, 480);

        var response = await ChangeBookAsync(fixture.ClientFor("edition-intruder"), entryId, other);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static Task<HttpResponseMessage> ChangeBookAsync(HttpClient client, Guid entryId, Guid bookId) =>
        client.PutAsJsonAsync($"/api/library/{entryId}/book", new { bookId });

    private static Task<HttpResponseMessage> LogAsync(HttpClient client, Guid entryId, decimal amount) =>
        client.PostAsJsonAsync($"/api/library/{entryId}/sessions", new { amount });

    private static async Task<Entry> EntryAsync(HttpClient client, Guid entryId) =>
        (await client.GetFromJsonAsync<IReadOnlyList<Entry>>("/api/library"))!.Single(e => e.Id == entryId);

    private sealed record Entry(Guid Id, Guid BookId, string Status, int? PageCountOverride, int? EffectivePageCount, Progress? Progress);

    private sealed record Progress(decimal? AmountRead, string? Unit, int? PercentComplete);

    private sealed record Session(Guid Id);

    private sealed record ValidationProblem(Dictionary<string, string[]>? Errors);
}
