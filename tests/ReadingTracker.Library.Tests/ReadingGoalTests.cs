using System.Net;
using System.Net.Http.Json;

namespace ReadingTracker.Library.Tests;

/// <summary>
/// How many books a reader means to finish in a year, and how many they have: the goal is
/// theirs to set, the count is worked out from when their books were finished.
/// </summary>
[Collection(LibraryApiCollection.Name)]
public sealed class ReadingGoalTests(LibraryApiFixture fixture)
{
    [Fact]
    public async Task A_year_with_no_goal_still_says_how_many_were_finished()
    {
        var client = fixture.ClientFor("no-goal-reader");
        var entryId = await fixture.AddBookAsync(client);
        await FinishAsync(client, entryId, "2026-05-15");

        var goal = await client.GetFromJsonAsync<Goal>("/api/library/goals/2026");

        Assert.Equal(2026, goal!.Year);
        Assert.Null(goal.Books);
        Assert.Equal(1, goal.Finished);
    }

    [Fact]
    public async Task Sets_a_goal_and_counts_only_the_books_finished_that_year()
    {
        var client = fixture.ClientFor("goal-reader");
        await FinishAsync(client, await fixture.AddBookAsync(client), "2026-01-02");
        await FinishAsync(client, await fixture.AddBookAsync(client), "2026-08-30");
        await FinishAsync(client, await fixture.AddBookAsync(client), "2025-12-31");
        await fixture.AddBookAsync(client); // want to read: not finished at all

        var response = await client.PutAsJsonAsync("/api/library/goals/2026", new { books = 24 });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var goal = (await response.Content.ReadFromJsonAsync<Goal>())!;
        Assert.Equal(24, goal.Books);
        Assert.Equal(2, goal.Finished);
    }

    [Fact]
    public async Task Changes_the_goal_in_place()
    {
        var client = fixture.ClientFor("changing-goal-reader");
        await client.PutAsJsonAsync("/api/library/goals/2026", new { books = 12 });

        await client.PutAsJsonAsync("/api/library/goals/2026", new { books = 20 });

        var goal = await client.GetFromJsonAsync<Goal>("/api/library/goals/2026");
        Assert.Equal(20, goal!.Books);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    [InlineData(1001)]
    public async Task A_goal_is_a_number_of_books_a_person_could_read(int books)
    {
        var client = fixture.ClientFor("silly-goal-reader");

        var response = await client.PutAsJsonAsync("/api/library/goals/2026", new { books });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Lets_the_goal_go()
    {
        var client = fixture.ClientFor("goal-gone-reader");
        await client.PutAsJsonAsync("/api/library/goals/2026", new { books = 12 });

        var response = await client.DeleteAsync("/api/library/goals/2026");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var goal = await client.GetFromJsonAsync<Goal>("/api/library/goals/2026");
        Assert.Null(goal!.Books);
    }

    [Fact]
    public async Task One_readers_goal_is_not_anothers()
    {
        var mine = fixture.ClientFor("goal-owner");
        var theirs = fixture.ClientFor("goal-neighbour");
        await mine.PutAsJsonAsync("/api/library/goals/2026", new { books = 12 });

        var goal = await theirs.GetFromJsonAsync<Goal>("/api/library/goals/2026");

        Assert.Null(goal!.Books);
    }

    private static async Task FinishAsync(HttpClient client, Guid entryId, string finishedOn)
    {
        var response = await client.PutAsJsonAsync($"/api/library/{entryId}/status", new { status = "Finished", finishedOn });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private sealed record Goal(int Year, int? Books, int Finished);
}
