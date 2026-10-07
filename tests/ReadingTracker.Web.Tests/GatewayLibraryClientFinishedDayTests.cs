using System.Text.Json;
using ReadingTracker.Web.Services;

namespace ReadingTracker.Web.Tests;

/// <summary>
/// The day a book was finished, as the shelf says it to Library: the Reader's own today unless
/// told otherwise, a day they state, or none at all when nobody knows it. Library falls back to
/// the UTC date only for callers that say nothing, and nothing in the browser says nothing.
/// </summary>
public sealed class GatewayLibraryClientFinishedDayTests
{
    private static readonly Guid EntryId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid BookId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    /// <summary>Half past midnight on New Year's Day at UTC+3, still New Year's Eve in UTC.</summary>
    private static readonly ReaderClock JustAfterMidnightAtPlusThree = new(
        new DateTimeOffset(2025, 12, 31, 21, 30, 0, TimeSpan.Zero),
        TimeZoneInfo.CreateCustomTimeZone("UTC+3", TimeSpan.FromHours(3), "UTC+3", "UTC+3"));

    [Fact]
    public async Task Finishing_a_book_says_the_readers_own_day_not_the_utc_one()
    {
        var (client, statusBodies) = CreateClient();

        await client.SetStatusAsync(EntryId, "Finished", CancellationToken.None);

        var body = Assert.Single(statusBodies);
        Assert.Equal("2026-01-01", body.GetProperty("finishedOn").GetString());
        Assert.False(body.TryGetProperty("dayUnknown", out _));
    }

    [Fact]
    public async Task A_day_the_reader_states_goes_as_it_is()
    {
        var (client, statusBodies) = CreateClient();

        await client.SetStatusAsync(EntryId, "Finished", CancellationToken.None, new FinishedDay.On(new DateOnly(2025, 8, 9)));

        Assert.Equal("2025-08-09", Assert.Single(statusBodies).GetProperty("finishedOn").GetString());
    }

    [Fact]
    public async Task A_day_nobody_knows_is_said_to_be_unknown_with_no_day_beside_it()
    {
        var (client, statusBodies) = CreateClient();

        await client.SetStatusAsync(EntryId, "Finished", CancellationToken.None, FinishedDay.Unknown);

        var body = Assert.Single(statusBodies);
        Assert.True(body.GetProperty("dayUnknown").GetBoolean());
        Assert.False(body.TryGetProperty("finishedOn", out _));
    }

    [Fact]
    public async Task Any_other_status_says_no_day()
    {
        var (client, statusBodies) = CreateClient();

        await client.SetStatusAsync(EntryId, "Reading", CancellationToken.None);

        var body = Assert.Single(statusBodies);
        Assert.False(body.TryGetProperty("finishedOn", out _));
        Assert.False(body.TryGetProperty("dayUnknown", out _));
    }

    [Fact]
    public async Task Adding_a_book_as_finished_says_the_readers_own_day()
    {
        var (client, statusBodies) = CreateClient();

        await client.AddAsAsync(BookId, "Finished", CancellationToken.None);

        Assert.Equal("2026-01-01", Assert.Single(statusBodies).GetProperty("finishedOn").GetString());
    }

    /// <summary>The client, and every status request body it sends, read as JSON.</summary>
    private static (GatewayLibraryClient Client, List<JsonElement> StatusBodies) CreateClient()
    {
        var statusBodies = new List<JsonElement>();
        var gateway = new StubHttpMessageHandler
        {
            RespondAsync = async (request, cancellationToken) =>
            {
                if (request.RequestUri!.AbsolutePath.EndsWith("/status"))
                {
                    statusBodies.Add(JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken)).RootElement.Clone());
                    return StubHttpMessageHandler.Json(Entry("Finished"));
                }

                return StubHttpMessageHandler.Json(Entry("WantToRead"));
            },
        };
        var httpClient = new HttpClient(gateway) { BaseAddress = new Uri("http://gateway.test/") };
        return (new GatewayLibraryClient(httpClient, new ShelfChanges(), JustAfterMidnightAtPlusThree), statusBodies);
    }

    private static string Entry(string status) =>
        $$"""{"id":"{{EntryId}}","bookId":"{{BookId}}","status":"{{status}}","trackingMethod":"Pages","addedAt":"2025-12-01T00:00:00Z"}""";

    /// <summary>A Reader's clock: a fixed instant, and the zone they are in.</summary>
    private sealed class ReaderClock(DateTimeOffset now, TimeZoneInfo zone) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;

        public override TimeZoneInfo LocalTimeZone => zone;
    }
}
