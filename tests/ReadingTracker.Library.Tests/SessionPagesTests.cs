using System.Net.Http.Json;

namespace ReadingTracker.Library.Tests;

/// <summary>
/// A session says how many pages it came to, so a client never has to work the conversion out
/// for itself — the same rule the reading-stats endpoint already applies. Unrounded, because
/// these are added up; absent when it was logged in percent of a book nobody knows the length of.
/// </summary>
[Collection(LibraryApiCollection.Name)]
public sealed class SessionPagesTests(LibraryApiFixture fixture)
{
    [Fact]
    public async Task A_session_logged_in_pages_came_to_those_pages()
    {
        var client = fixture.ClientFor("pages-session-reader");
        var entryId = await fixture.AddBookAsync(client, 300);

        await LogAsync(client, entryId, 22);

        Assert.Equal(22m, Assert.Single(await SessionsAsync(client, entryId)).Pages);
    }

    [Fact]
    public async Task A_session_logged_in_percent_came_to_that_share_of_the_pages_unrounded()
    {
        var client = fixture.ClientFor("percent-session-reader");
        var entryId = await fixture.AddBookAsync(client, 300);
        await TrackByAsync(client, entryId, "Percentage");

        await LogAsync(client, entryId, 12.5m);

        var session = Assert.Single(await SessionsAsync(client, entryId));
        Assert.Equal(12.5m, session.Amount);
        Assert.Equal("Percentage", session.Unit);
        Assert.Equal(37.5m, session.Pages);
    }

    [Fact]
    public async Task A_session_in_percent_of_a_book_of_no_known_length_has_no_pages()
    {
        var client = fixture.ClientFor("no-length-session-reader");
        var entryId = await fixture.AddBookAsync(client, totalPages: null);
        await TrackByAsync(client, entryId, "Percentage");

        await LogAsync(client, entryId, 20);

        Assert.Null(Assert.Single(await SessionsAsync(client, entryId)).Pages);
    }

    [Fact]
    public async Task A_session_in_percent_follows_the_readers_own_page_count()
    {
        var client = fixture.ClientFor("own-count-session-reader");
        var entryId = await fixture.AddBookAsync(client, 300);
        await TrackByAsync(client, entryId, "Percentage");
        await LogAsync(client, entryId, 50);

        // The edition in their hands is longer than the Catalog says: their count is the one used.
        await client.PutAsJsonAsync($"/api/library/{entryId}/page-count", new { totalPages = 400 });

        Assert.Equal(200m, Assert.Single(await SessionsAsync(client, entryId)).Pages);
    }

    [Fact]
    public async Task A_session_is_not_stopped_at_the_end_of_the_book_in_its_pages_either()
    {
        var client = fixture.ClientFor("past-the-end-session-reader");
        var entryId = await fixture.AddBookAsync(client, 300);
        await TrackByAsync(client, entryId, "Percentage");

        await LogAsync(client, entryId, 140);

        // Only the total stops at the whole book; the session is kept exactly as it was stated.
        Assert.Equal(420m, Assert.Single(await SessionsAsync(client, entryId)).Pages);
    }

    [Fact]
    public async Task Logging_a_session_answers_with_its_pages_too()
    {
        var client = fixture.ClientFor("logged-answer-reader");
        var entryId = await fixture.AddBookAsync(client, 300);
        await TrackByAsync(client, entryId, "Percentage");

        var response = await LogAsync(client, entryId, 10);

        var logged = await response.Content.ReadFromJsonAsync<Logged>();
        Assert.Equal(30m, logged!.Session.Pages);
    }

    private static Task<HttpResponseMessage> TrackByAsync(HttpClient client, Guid entryId, string method) =>
        client.PutAsJsonAsync($"/api/library/{entryId}/tracking-method", new { trackingMethod = method });

    private static Task<HttpResponseMessage> LogAsync(HttpClient client, Guid entryId, decimal amount) =>
        client.PostAsJsonAsync($"/api/library/{entryId}/sessions", new { amount });

    private static async Task<IReadOnlyList<Session>> SessionsAsync(HttpClient client, Guid entryId) =>
        (await client.GetFromJsonAsync<IReadOnlyList<Session>>($"/api/library/{entryId}/sessions"))!;

    private sealed record Session(Guid Id, decimal Amount, string Unit, decimal? Pages);

    private sealed record Logged(Session Session);
}
