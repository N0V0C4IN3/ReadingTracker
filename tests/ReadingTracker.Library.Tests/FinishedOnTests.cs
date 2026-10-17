using System.Net;
using System.Net.Http.Json;

namespace ReadingTracker.Library.Tests;

/// <summary>
/// When a book was finished: stamped the day it is marked Finished, or said outright — an
/// import knows the real day — and let go of when the book is un-finished. The yearly goal
/// counts by it.
/// </summary>
[Collection(LibraryApiCollection.Name)]
public sealed class FinishedOnTests(LibraryApiFixture fixture)
{
    [Fact]
    public async Task Marking_a_book_finished_stamps_today()
    {
        var client = fixture.ClientFor("finished-today-reader");
        var entryId = await fixture.AddBookAsync(client);

        var entry = await SetStatusAsync(client, entryId, new { status = "Finished" });

        Assert.Equal(DateOnly.FromDateTime(DateTime.UtcNow), entry.FinishedOn);
    }

    [Fact]
    public async Task Takes_the_day_i_say_i_finished()
    {
        var client = fixture.ClientFor("finished-then-reader");
        var entryId = await fixture.AddBookAsync(client);

        var entry = await SetStatusAsync(client, entryId, new { status = "Finished", finishedOn = "2025-08-09" });

        Assert.Equal(new DateOnly(2025, 8, 9), entry.FinishedOn);
    }

    [Fact]
    public async Task Corrects_the_day_on_a_book_already_finished()
    {
        var client = fixture.ClientFor("corrected-day-reader");
        var entryId = await fixture.AddBookAsync(client);
        await SetStatusAsync(client, entryId, new { status = "Finished" });

        var entry = await SetStatusAsync(client, entryId, new { status = "Finished", finishedOn = "2024-12-31" });

        Assert.Equal(new DateOnly(2024, 12, 31), entry.FinishedOn);
    }

    [Fact]
    public async Task Forgets_the_day_when_the_book_is_no_longer_finished_and_stamps_anew_when_it_is_again()
    {
        var client = fixture.ClientFor("unfinished-reader");
        var entryId = await fixture.AddBookAsync(client);
        await SetStatusAsync(client, entryId, new { status = "Finished", finishedOn = "2025-08-09" });

        var reading = await SetStatusAsync(client, entryId, new { status = "Reading" });
        Assert.Null(reading.FinishedOn);

        var finished = await SetStatusAsync(client, entryId, new { status = "Finished" });
        Assert.Equal(DateOnly.FromDateTime(DateTime.UtcNow), finished.FinishedOn);
    }

    [Fact]
    public async Task A_day_that_has_not_happened_is_refused()
    {
        var client = fixture.ClientFor("future-reader");
        var entryId = await fixture.AddBookAsync(client);
        var tomorrow = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(2).ToString("yyyy-MM-dd");

        var response = await client.PutAsJsonAsync($"/api/library/{entryId}/status", new { status = "Finished", finishedOn = tomorrow });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_day_only_goes_with_finished()
    {
        var client = fixture.ClientFor("day-without-finished-reader");
        var entryId = await fixture.AddBookAsync(client);

        var response = await client.PutAsJsonAsync($"/api/library/{entryId}/status", new { status = "Reading", finishedOn = "2025-08-09" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    /// An import can say a book was read without saying when. It is Finished, with no day, and
    /// counts toward no year's goal rather than this one's.
    /// </summary>
    [Fact]
    public async Task A_book_finished_on_a_day_nobody_knows_has_no_day_and_counts_toward_no_year()
    {
        var client = fixture.ClientFor("day-unknown-reader");
        var entryId = await fixture.AddBookAsync(client);

        var entry = await SetStatusAsync(client, entryId, new { status = "Finished", dayUnknown = true });

        Assert.Equal("Finished", entry.Status);
        Assert.Null(entry.FinishedOn);
        var goal = await client.GetFromJsonAsync<Goal>($"/api/library/goals/{DateTime.UtcNow.Year}");
        Assert.Equal(0, goal!.Finished);
    }

    [Fact]
    public async Task Saying_the_day_is_unknown_on_a_finished_book_lets_its_day_go()
    {
        var client = fixture.ClientFor("forgot-the-day-reader");
        var entryId = await fixture.AddBookAsync(client);
        await SetStatusAsync(client, entryId, new { status = "Finished", finishedOn = "2025-08-09" });

        var entry = await SetStatusAsync(client, entryId, new { status = "Finished", dayUnknown = true });

        Assert.Null(entry.FinishedOn);
    }

    [Theory]
    [InlineData("Reading", null)]
    [InlineData("Finished", "2025-08-09")]
    public async Task An_unknown_day_goes_only_with_finished_and_never_beside_a_day(string status, string? finishedOn)
    {
        var client = fixture.ClientFor($"unknown-day-refused-{status}");
        var entryId = await fixture.AddBookAsync(client);

        var response = await client.PutAsJsonAsync($"/api/library/{entryId}/status", new { status, finishedOn, dayUnknown = true });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<Problem>();
        Assert.True(problem!.Errors.ContainsKey("dayUnknown"));
    }

    [Fact]
    public async Task A_device_reporting_on_a_finished_book_leaves_the_day_alone()
    {
        var client = fixture.ClientFor("reread-day-reader");
        var entryId = await fixture.AddBookAsync(client);
        await SetStatusAsync(client, entryId, new { status = "Finished", finishedOn = "2025-08-09" });

        await client.PutAsJsonAsync($"/api/library/{entryId}/bookmark", new { percent = 30 });

        var entries = await client.GetFromJsonAsync<IReadOnlyList<Entry>>("/api/library");
        Assert.Equal(new DateOnly(2025, 8, 9), Assert.Single(entries!).FinishedOn);
    }

    private static async Task<Entry> SetStatusAsync(HttpClient client, Guid entryId, object body)
    {
        var response = await client.PutAsJsonAsync($"/api/library/{entryId}/status", body);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<Entry>())!;
    }

    private sealed record Entry(Guid Id, string Status, DateOnly? FinishedOn);

    private sealed record Goal(int Year, int? Books, int Finished);

    private sealed record Problem(Dictionary<string, string[]> Errors);
}
