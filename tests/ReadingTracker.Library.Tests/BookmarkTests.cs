using System.Net;
using System.Net.Http.Json;

namespace ReadingTracker.Library.Tests;

/// <summary>
/// A device says where the reader is; Library works out what that means. The Bookmark is the
/// "where", ReadingSessions are the "how much", and a report moving the one forward is what
/// makes the other (ADR-0015).
/// </summary>
[Collection(LibraryApiCollection.Name)]
public sealed class BookmarkTests(LibraryApiFixture fixture)
{
    [Fact]
    public async Task A_first_report_on_an_untouched_book_is_all_reading()
    {
        var client = fixture.ClientFor("first-report-reader");
        var entryId = await fixture.AddBookAsync(client, 300);

        var reported = await ReportAsync(client, entryId, 40);

        Assert.Equal(HttpStatusCode.OK, reported.StatusCode);
        var answer = (await reported.Content.ReadFromJsonAsync<Reported>())!;
        Assert.Equal(40, answer.Session!.Amount);
        Assert.Equal("Percentage", answer.Session.Unit);
        Assert.Equal("Device", answer.Session.Source);
        Assert.Equal(40, answer.Entry.Bookmark!.Percent);
        Assert.Equal(40, answer.Entry.Progress!.PercentComplete);
    }

    [Fact]
    public async Task A_first_report_past_what_i_logged_by_hand_credits_only_the_difference()
    {
        var client = fixture.ClientFor("caught-up-reader");
        var entryId = await fixture.AddBookAsync(client, 200);
        await LogAsync(client, entryId, 60); // 30% by hand

        var answer = await ReportAndReadAsync(client, entryId, 40);

        // The tracker knew about 30%; the device says 40%. Ten percent it did not know about.
        Assert.Equal(10, answer.Session!.Amount);
        Assert.Equal(40, answer.Entry.Progress!.PercentComplete);
    }

    [Fact]
    public async Task A_first_report_behind_what_i_logged_by_hand_sets_the_bookmark_and_nothing_else()
    {
        var client = fixture.ClientFor("behind-reader");
        var entryId = await fixture.AddBookAsync(client, 200);
        await LogAsync(client, entryId, 100); // 50% by hand

        var answer = await ReportAndReadAsync(client, entryId, 20);

        // A position behind the total is not un-reading: the Bookmark says where the device is,
        // and what the reader recorded stays recorded.
        Assert.Null(answer.Session);
        Assert.Equal(20, answer.Entry.Bookmark!.Percent);
        Assert.Equal(50, answer.Entry.Progress!.PercentComplete);
    }

    [Fact]
    public async Task Measures_a_first_report_from_the_start_when_pages_were_logged_and_nobody_knows_the_length()
    {
        var client = fixture.ClientFor("unknown-length-reader");
        var entryId = await fixture.AddBookAsync(client, totalPages: null);
        await LogAsync(client, entryId, 80); // pages, of a book of unknown length

        var answer = await ReportAndReadAsync(client, entryId, 25);

        // Eighty pages of an unknown length is no particular percentage, so there is nothing to
        // measure against but the start. The pages stay on the record alongside.
        Assert.Equal(25, answer.Session!.Amount);
        Assert.Equal(25, answer.Entry.Bookmark!.Percent);
    }

    [Fact]
    public async Task Forward_logs_the_difference_and_backward_logs_nothing()
    {
        var client = fixture.ClientFor("to-and-fro-reader");
        var entryId = await fixture.AddBookAsync(client, 300);
        await ReportAsync(client, entryId, 30);

        var forward = await ReportAndReadAsync(client, entryId, 45);
        var backward = await ReportAndReadAsync(client, entryId, 20);
        var forwardAgain = await ReportAndReadAsync(client, entryId, 32);

        Assert.Equal(15, forward.Session!.Amount);
        // Flipping back is not reading; the Bookmark follows, the total does not.
        Assert.Null(backward.Session);
        Assert.Equal(20, backward.Entry.Bookmark!.Percent);
        Assert.Equal(45, backward.Entry.Progress!.PercentComplete);
        // Reading on from where the device now is: measured from the Bookmark, not the total.
        Assert.Equal(12, forwardAgain.Session!.Amount);
        Assert.Equal(57, forwardAgain.Entry.Progress!.PercentComplete);
    }

    [Theory]
    [InlineData("WantToRead")]
    [InlineData("OnHold")]
    [InlineData("Dropped")]
    public async Task A_report_means_the_book_is_being_read(string before)
    {
        await using var listener = await LibraryEventListener.StartAsync(fixture.RabbitMqConnectionString);
        var client = fixture.ClientFor($"picks-it-up-{before}");
        var entryId = await fixture.AddBookAsync(client, 300);
        await client.PutAsJsonAsync($"/api/library/{entryId}/status", new { status = before });

        var answer = await ReportAndReadAsync(client, entryId, 10);

        Assert.Equal("Reading", answer.Entry.Status);
        // Announced like any other change of status, so nothing listening can tell a device from
        // a reader.
        ReadingStatusChangedMessage announced;
        do
        {
            announced = await listener.NextAsync();
        }
        while (announced.LibraryEntryId != entryId || announced.NewStatus != "Reading");
        Assert.Equal(before, announced.PreviousStatus);
    }

    [Theory]
    [InlineData("Reading")]
    [InlineData("Finished")]
    public async Task A_report_leaves_a_book_being_read_or_already_finished_as_it_is(string status)
    {
        var client = fixture.ClientFor($"stays-put-{status}");
        var entryId = await fixture.AddBookAsync(client, 300);
        await client.PutAsJsonAsync($"/api/library/{entryId}/status", new { status });

        var answer = await ReportAndReadAsync(client, entryId, 10);

        // A re-read of a Finished book is sessions, not a status: nothing un-finishes it.
        Assert.Equal(status, answer.Entry.Status);
        Assert.Equal(10, answer.Session!.Amount);
    }

    [Fact]
    public async Task Sessions_say_who_reported_them()
    {
        var client = fixture.ClientFor("provenance-reader");
        var entryId = await fixture.AddBookAsync(client, 300);
        await LogAsync(client, entryId, 30);
        await ReportAsync(client, entryId, 50);

        var sessions = await SessionsAsync(client, entryId);

        Assert.Equal(["Device", "Reader"], sessions.Select(session => session.Source));
    }

    [Fact]
    public async Task Correcting_a_devices_session_keeps_it_a_devices_session()
    {
        var client = fixture.ClientFor("correcting-reader");
        var entryId = await fixture.AddBookAsync(client, 300);
        var reported = (await ReportAndReadAsync(client, entryId, 50)).Session!;

        var corrected = await client.PutAsJsonAsync(
            $"/api/library/{entryId}/sessions/{reported.Id}",
            new { amount = 120 });

        Assert.Equal(HttpStatusCode.OK, corrected.StatusCode);
        var session = Assert.Single(await SessionsAsync(client, entryId));
        Assert.Equal(120, session.Amount);
        Assert.Equal("Device", session.Source);
    }

    [Fact]
    public async Task Correcting_sessions_leaves_the_bookmark_where_the_device_put_it()
    {
        var client = fixture.ClientFor("bookmark-stays-reader");
        var entryId = await fixture.AddBookAsync(client, 300);
        var reported = (await ReportAndReadAsync(client, entryId, 50)).Session!;

        await client.DeleteAsync($"/api/library/{entryId}/sessions/{reported.Id}");

        var entry = await GetEntryAsync(client, entryId);
        Assert.Null(entry.Progress);
        Assert.Equal(50, entry.Bookmark!.Percent);
    }

    [Fact]
    public async Task Stamps_the_session_with_when_the_reading_happened_not_when_it_was_reported()
    {
        var client = fixture.ClientFor("late-syncing-reader");
        var entryId = await fixture.AddBookAsync(client, 300);
        var readAt = new DateTimeOffset(2026, 9, 18, 22, 15, 0, TimeSpan.FromHours(3));

        var answer = await ReportAndReadAsync(client, entryId, 30, occurredAt: readAt);

        Assert.Equal(readAt, answer.Session!.OccurredAt);
        Assert.Equal(readAt, answer.Entry.Bookmark!.ReportedAt);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(100.5)]
    public async Task Refuses_a_position_that_is_not_in_the_book(decimal percent)
    {
        var client = fixture.ClientFor("off-the-page-reader");
        var entryId = await fixture.AddBookAsync(client, 300);

        var response = await ReportAsync(client, entryId, percent);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null((await GetEntryAsync(client, entryId)).Bookmark);
    }

    [Fact]
    public async Task Refuses_a_report_that_does_not_say_where()
    {
        var client = fixture.ClientFor("says-nothing-reader");
        var entryId = await fixture.AddBookAsync(client, 300);

        // A body with no percent must not read as "the start of the book".
        var response = await client.PutAsJsonAsync($"/api/library/{entryId}/bookmark", new { });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null((await GetEntryAsync(client, entryId)).Bookmark);
    }

    [Fact]
    public async Task Will_not_take_a_first_report_while_the_books_length_cannot_be_looked_up()
    {
        var client = fixture.ClientFor("catalog-down-reader");
        var entryId = await fixture.AddBookAsync(client, 200);
        await LogAsync(client, entryId, 100); // 50% by hand, in pages
        fixture.Catalog.Respond = _ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);

        var refused = await ReportAsync(client, entryId, 60);

        // Measuring 60% against pages of a book whose length nobody can say right now would
        // credit the whole 60% and count the hand-logged half twice, for good.
        Assert.Equal(HttpStatusCode.ServiceUnavailable, refused.StatusCode);
        fixture.CatalogHasBook(Guid.NewGuid(), 200);
        Assert.Null((await GetEntryAsync(client, entryId)).Bookmark);
    }

    [Fact]
    public async Task Takes_a_later_report_whether_or_not_catalog_answers()
    {
        var client = fixture.ClientFor("catalog-flaky-reader");
        var entryId = await fixture.AddBookAsync(client, 200);
        await ReportAsync(client, entryId, 30);
        fixture.Catalog.Respond = _ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);

        var answer = await ReportAndReadAsync(client, entryId, 45);

        // Once the Bookmark exists the page count is not needed: the difference is percent to percent.
        Assert.Equal(15, answer.Session!.Amount);
        Assert.Null(answer.Entry.Book);
        fixture.CatalogHasBook(Guid.NewGuid(), 200);
    }

    [Fact]
    public async Task Does_not_find_another_readers_book()
    {
        var owner = fixture.ClientFor("bookmark-owner");
        var entryId = await fixture.AddBookAsync(owner, 300);

        var response = await ReportAsync(fixture.ClientFor("bookmark-stranger"), entryId, 30);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Null((await GetEntryAsync(owner, entryId)).Bookmark);
    }

    [Fact]
    public async Task A_book_no_device_has_reported_has_no_bookmark()
    {
        var client = fixture.ClientFor("no-device-reader");
        var entryId = await fixture.AddBookAsync(client, 300);
        await LogAsync(client, entryId, 30);

        Assert.Null((await GetEntryAsync(client, entryId)).Bookmark);
    }

    private static async Task<Reported> ReportAndReadAsync(
        HttpClient client,
        Guid entryId,
        decimal percent,
        DateTimeOffset? occurredAt = null)
    {
        var response = await ReportAsync(client, entryId, percent, occurredAt);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<Reported>())!;
    }

    private static Task<HttpResponseMessage> LogAsync(HttpClient client, Guid entryId, decimal amount) =>
        client.PostAsJsonAsync($"/api/library/{entryId}/sessions", new { amount });

    private static async Task<IReadOnlyList<Session>> SessionsAsync(HttpClient client, Guid entryId) =>
        (await client.GetFromJsonAsync<IReadOnlyList<Session>>($"/api/library/{entryId}/sessions"))!;

    private static async Task<Entry> GetEntryAsync(HttpClient client, Guid entryId) =>
        (await client.GetFromJsonAsync<IReadOnlyList<Entry>>("/api/library"))!.Single(e => e.Id == entryId);

    private static Task<HttpResponseMessage> ReportAsync(
        HttpClient client,
        Guid entryId,
        decimal percent,
        DateTimeOffset? occurredAt = null) =>
        client.PutAsJsonAsync($"/api/library/{entryId}/bookmark", new
        {
            percent,
            occurredAt,
        });

    private sealed record Reported(Session? Session, Entry Entry);

    private sealed record Entry(Guid Id, string Status, Progress? Progress, Bookmark? Bookmark, Book? Book);

    private sealed record Book(string Title);

    private sealed record Bookmark(decimal Percent, DateTimeOffset ReportedAt);

    private sealed record Progress(decimal? AmountRead, string? Unit, int? PercentComplete);

    private sealed record Session(
        Guid Id,
        decimal Amount,
        string Unit,
        string Source,
        DateTimeOffset OccurredAt,
        int? DurationMinutes);
}
