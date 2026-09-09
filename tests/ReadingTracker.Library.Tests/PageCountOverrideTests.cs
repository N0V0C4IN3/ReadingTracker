using System.Net;
using System.Net.Http.Json;

namespace ReadingTracker.Library.Tests;

[Collection(LibraryApiCollection.Name)]
public sealed class PageCountOverrideTests(LibraryApiFixture fixture)
{
    [Fact]
    public async Task Fixes_a_page_count_the_catalog_got_wrong_for_my_edition()
    {
        var client = fixture.ClientFor("wrong-count-reader");
        var entryId = await fixture.AddBookAsync(client, 300);
        await LogAsync(client, entryId, 120);

        var response = await SetPageCountAsync(client, entryId, 705);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var entry = await EntryAsync(client, entryId);
        Assert.Equal(705, entry.EffectivePageCount);
        // 120 of 705, not of the 300 the catalog claimed.
        Assert.Equal(17, entry.Progress!.PercentComplete);
    }

    [Fact]
    public async Task Goes_back_to_the_catalogs_count_when_i_clear_my_correction()
    {
        var client = fixture.ClientFor("cleared-count-reader");
        var entryId = await fixture.AddBookAsync(client, 300);
        await LogAsync(client, entryId, 120);
        await SetPageCountAsync(client, entryId, 705);

        await SetPageCountAsync(client, entryId, null);

        var entry = await EntryAsync(client, entryId);
        Assert.Null(entry.PageCountOverride);
        Assert.Equal(300, entry.EffectivePageCount);
        Assert.Equal(40, entry.Progress!.PercentComplete);
    }

    [Fact]
    public async Task Lets_me_track_a_book_whose_length_nobody_knows()
    {
        var client = fixture.ClientFor("unknown-count-reader");
        var entryId = await fixture.AddBookAsync(client, totalPages: null);
        await LogAsync(client, entryId, 100);

        // Without a count there is no percentage to give...
        Assert.Null((await EntryAsync(client, entryId)).Progress!.PercentComplete);

        await SetPageCountAsync(client, entryId, 400);

        // ...and supplying one is all it takes.
        Assert.Equal(25, (await EntryAsync(client, entryId)).Progress!.PercentComplete);
    }

    [Fact]
    public async Task Uses_my_count_when_converting_between_pages_and_percentage()
    {
        var client = fixture.ClientFor("converting-count-reader");
        var entryId = await fixture.AddBookAsync(client, 300);
        await LogAsync(client, entryId, 120);
        await SetPageCountAsync(client, entryId, 480);

        await client.PutAsJsonAsync($"/api/library/{entryId}/tracking-method", new { trackingMethod = "Percentage" });

        // 120 of 480 is a quarter of the way in.
        Assert.Equal(25, (await EntryAsync(client, entryId)).Progress!.AmountRead);
    }

    [Fact]
    public async Task Keeps_my_correction_to_myself()
    {
        var bookId = Guid.NewGuid();
        fixture.CatalogHasBook(bookId, 300);
        var mine = fixture.ClientFor("overriding-reader");
        var theirs = fixture.ClientFor("unaffected-reader");
        var myEntry = await AddAsync(mine, bookId);
        var theirEntry = await AddAsync(theirs, bookId);

        await SetPageCountAsync(mine, myEntry, 705);

        // Same Book, two readers, one correction: the Book is shared, the correction is not.
        Assert.Equal(705, (await EntryAsync(mine, myEntry)).EffectivePageCount);
        Assert.Equal(300, (await EntryAsync(theirs, theirEntry)).EffectivePageCount);
    }

    [Fact]
    public async Task Never_writes_my_correction_back_to_the_shared_catalog()
    {
        var client = fixture.ClientFor("no-writeback-reader");
        var entryId = await fixture.AddBookAsync(client, 300);
        fixture.Catalog.Requests.Clear();

        await SetPageCountAsync(client, entryId, 705);
        await EntryAsync(client, entryId);

        // Library only ever reads from Catalog. A reader's own page count is theirs alone,
        // and correcting it must not change the book every other reader sees.
        Assert.All(fixture.Catalog.Requests, request => Assert.Equal(HttpMethod.Get, request.Method));
    }

    [Fact]
    public async Task Rejects_a_page_count_that_is_not_a_length_a_book_could_have()
    {
        var client = fixture.ClientFor("silly-count-reader");
        var entryId = await fixture.AddBookAsync(client, 300);

        var zero = await SetPageCountAsync(client, entryId, 0);
        var negative = await SetPageCountAsync(client, entryId, -10);

        Assert.Equal(HttpStatusCode.BadRequest, zero.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, negative.StatusCode);
        Assert.Equal(300, (await EntryAsync(client, entryId)).EffectivePageCount);
    }

    [Fact]
    public async Task Refuses_to_correct_the_page_count_on_someone_elses_shelf()
    {
        var entryId = await fixture.AddBookAsync(fixture.ClientFor("count-owner"), 300);

        var response = await SetPageCountAsync(fixture.ClientFor("count-intruder"), entryId, 705);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static Task<HttpResponseMessage> SetPageCountAsync(HttpClient client, Guid entryId, int? totalPages) =>
        client.PutAsJsonAsync($"/api/library/{entryId}/page-count", new { totalPages });

    private static Task<HttpResponseMessage> LogAsync(HttpClient client, Guid entryId, decimal amount) =>
        client.PostAsJsonAsync($"/api/library/{entryId}/sessions", new { amount });

    private static async Task<Guid> AddAsync(HttpClient client, Guid bookId) =>
        (await (await client.PostAsJsonAsync("/api/library", new { bookId })).Content.ReadFromJsonAsync<Entry>())!.Id;

    private static async Task<Entry> EntryAsync(HttpClient client, Guid entryId) =>
        (await client.GetFromJsonAsync<IReadOnlyList<Entry>>("/api/library"))!.Single(e => e.Id == entryId);

    private sealed record Entry(Guid Id, int? PageCountOverride, int? EffectivePageCount, Progress? Progress);

    private sealed record Progress(decimal? AmountRead, string? Unit, int? PercentComplete);
}
