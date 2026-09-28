using System.Net;
using System.Net.Http.Json;

namespace ReadingTracker.Library.Tests;

/// <summary>
/// A reader's reading over a stretch of time, across the whole shelf: what the stats page's
/// calendar and streaks are drawn from. Every session in the stretch, in pages, oldest first.
/// </summary>
[Collection(LibraryApiCollection.Name)]
public sealed class ReadingHistoryTests(LibraryApiFixture fixture)
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    [Fact]
    public async Task Lists_the_reading_on_every_book_in_the_stretch_oldest_first()
    {
        var client = fixture.ClientFor("reading-span-reader");
        var dune = await fixture.AddBookAsync(client, 400);
        var piranesi = await fixture.AddBookAsync(client, 250);
        await LogAsync(client, dune, 30, Now.AddDays(-2), minutes: 40);
        await LogAsync(client, piranesi, 12, Now.AddDays(-1));
        await LogAsync(client, dune, 50, Now.AddDays(-40));

        var span = await ReadingAsync(client, Now.AddDays(-7), Now.AddMinutes(1));

        Assert.Equal([dune, piranesi], span.Sessions.Select(session => session.EntryId));
        Assert.Equal([30m, 12m], span.Sessions.Select(session => session.Pages));
        Assert.Equal(40, span.Sessions[0].DurationMinutes);
        Assert.All(span.Sessions, session => Assert.False(session.FromDevice));
    }

    [Fact]
    public async Task Names_each_book_read_once()
    {
        var client = fixture.ClientFor("reading-span-titles");
        var entry = await fixture.AddBookAsync(client);
        await LogAsync(client, entry, 10, Now.AddHours(-3));
        await LogAsync(client, entry, 10, Now.AddHours(-2));

        var book = Assert.Single((await ReadingAsync(client, Now.AddDays(-1), Now.AddMinutes(1))).Books);

        Assert.Equal(entry, book.EntryId);
        Assert.Equal("A Book", book.Title);
    }

    [Fact]
    public async Task Keeps_the_fraction_of_a_page_a_small_percent_comes_to()
    {
        var client = fixture.ClientFor("reading-span-fraction");
        var entry = await fixture.AddBookAsync(client, 300);
        await TrackInPercentAsync(client, entry);
        await LogAsync(client, entry, 0.1m, Now.AddHours(-1));

        // Rounded, a device's tenth of a percent would be no reading at all; added up over a day
        // of them it is a page or two.
        Assert.Equal(0.3m, Assert.Single((await ReadingAsync(client, Now.AddDays(-1), Now.AddMinutes(1))).Sessions).Pages);
    }

    [Fact]
    public async Task Leaves_out_everyone_elses_reading()
    {
        var mine = fixture.ClientFor("reading-span-mine");
        var theirs = fixture.ClientFor("reading-span-theirs");
        await LogAsync(theirs, await fixture.AddBookAsync(theirs), 20, Now.AddHours(-3));

        Assert.Empty((await ReadingAsync(mine, Now.AddDays(-1), Now.AddMinutes(1))).Sessions);
    }

    [Fact]
    public async Task Says_percent_in_pages_when_the_length_is_known()
    {
        var client = fixture.ClientFor("reading-span-percent-known");
        var entry = await fixture.AddBookAsync(client, 300);
        await TrackInPercentAsync(client, entry);
        await LogAsync(client, entry, 10, Now.AddHours(-1));

        Assert.Equal(30m, Assert.Single((await ReadingAsync(client, Now.AddDays(-1), Now.AddMinutes(1))).Sessions).Pages);
    }

    [Fact]
    public async Task Says_no_pages_for_percent_when_nobody_knows_the_length()
    {
        var client = fixture.ClientFor("reading-span-percent-unknown");
        var entry = await fixture.AddBookAsync(client, totalPages: null);
        await TrackInPercentAsync(client, entry);
        await LogAsync(client, entry, 10, Now.AddHours(-1));

        Assert.Null(Assert.Single((await ReadingAsync(client, Now.AddDays(-1), Now.AddMinutes(1))).Sessions).Pages);
    }

    [Theory]
    [InlineData("")]
    [InlineData("?from=2026-01-01T00:00:00Z")]
    [InlineData("?from=2026-02-01T00:00:00Z&to=2026-01-01T00:00:00Z")]
    [InlineData("?from=2024-01-01T00:00:00Z&to=2026-01-01T00:00:00Z")]
    public async Task Asks_for_a_stretch_of_at_most_a_year(string query)
    {
        var client = fixture.ClientFor("reading-span-asks");

        var response = await client.GetAsync($"/api/library/reading{query}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static async Task<Span> ReadingAsync(HttpClient client, DateTimeOffset from, DateTimeOffset to) =>
        (await client.GetFromJsonAsync<Span>(
            $"/api/library/reading?from={Uri.EscapeDataString(from.ToString("O"))}&to={Uri.EscapeDataString(to.ToString("O"))}"))!;

    private static async Task LogAsync(HttpClient client, Guid entryId, decimal amount, DateTimeOffset occurredAt, int? minutes = null) =>
        Assert.Equal(
            HttpStatusCode.Created,
            (await client.PostAsJsonAsync($"/api/library/{entryId}/sessions", new { amount, occurredAt, durationMinutes = minutes })).StatusCode);

    private static async Task TrackInPercentAsync(HttpClient client, Guid entryId) =>
        Assert.Equal(
            HttpStatusCode.OK,
            (await client.PutAsJsonAsync($"/api/library/{entryId}/tracking-method", new { trackingMethod = "Percentage" })).StatusCode);

    private sealed record Span(IReadOnlyList<Book> Books, IReadOnlyList<Session> Sessions);

    private sealed record Book(Guid EntryId, string? Title);

    private sealed record Session(Guid EntryId, DateTimeOffset OccurredAt, decimal? Pages, int? DurationMinutes, bool FromDevice);
}
