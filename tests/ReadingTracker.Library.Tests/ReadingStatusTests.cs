using System.Net;
using System.Net.Http.Json;

namespace ReadingTracker.Library.Tests;

[Collection(LibraryApiCollection.Name)]
public sealed class ReadingStatusTests(LibraryApiFixture fixture)
{
    [Fact]
    public async Task Moves_a_book_through_the_states_a_book_actually_goes_through()
    {
        var client = fixture.ClientFor("status-reader");
        var entryId = await fixture.AddBookAsync(client);

        foreach (var status in new[] { "Reading", "OnHold", "Reading", "Finished" })
        {
            var response = await client.PutAsJsonAsync($"/api/library/{entryId}/status", new { status });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(status, (await response.Content.ReadFromJsonAsync<Entry>())!.Status);
        }

        var entries = await client.GetFromJsonAsync<IReadOnlyList<Entry>>("/api/library");
        Assert.Equal("Finished", Assert.Single(entries!).Status);
    }

    [Fact]
    public async Task Shows_me_just_the_books_in_one_state()
    {
        var client = fixture.ClientFor("filter-reader");
        var reading = await fixture.AddBookAsync(client);
        var wanted = await fixture.AddBookAsync(client);
        await client.PutAsJsonAsync($"/api/library/{reading}/status", new { status = "Reading" });

        var currentlyReading = await client.GetFromJsonAsync<IReadOnlyList<Entry>>("/api/library?status=Reading");

        Assert.Equal(reading, Assert.Single(currentlyReading!).Id);
        Assert.DoesNotContain(currentlyReading!, entry => entry.Id == wanted);
    }

    [Fact]
    public async Task Announces_a_status_change_but_says_nothing_when_nothing_changed()
    {
        var client = fixture.ClientFor("announce-reader");
        var entryId = await fixture.AddBookAsync(client);
        await using var listener = await LibraryEventListener.StartAsync(fixture.RabbitMqConnectionString);

        await client.PutAsJsonAsync($"/api/library/{entryId}/status", new { status = "Reading" });

        var announced = await listener.NextAsync();
        Assert.Equal(entryId, announced.LibraryEntryId);
        Assert.Equal("announce-reader", announced.ReaderId);
        Assert.Equal("WantToRead", announced.PreviousStatus);
        Assert.Equal("Reading", announced.NewStatus);

        // Setting the status it already has is not a change, so there is nothing to announce.
        await client.PutAsJsonAsync($"/api/library/{entryId}/status", new { status = "Reading" });

        Assert.Null(await listener.NextOrNullAsync(TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public async Task Refuses_to_change_a_status_on_someone_elses_shelf()
    {
        var mine = await fixture.AddBookAsync(fixture.ClientFor("owner-reader"));

        var response = await fixture.ClientFor("intruder-reader")
            .PutAsJsonAsync($"/api/library/{mine}/status", new { status = "Finished" });

        // Not "forbidden": another reader's entry simply isn't part of this reader's library.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Rejects_a_status_that_is_not_a_reading_status()
    {
        var client = fixture.ClientFor("badstatus-reader");
        var entryId = await fixture.AddBookAsync(client);

        var response = await client.PutAsJsonAsync($"/api/library/{entryId}/status", new { status = "Abandoned" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private sealed record Entry(Guid Id, Guid BookId, string Status);
}
