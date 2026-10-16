using System.Net;
using ReadingTracker.Web.Services;

namespace ReadingTracker.Web.Tests;

/// <summary>
/// A book's Quotes, as the Book page reads and changes them: listed in the order Library puts
/// them in, a note edited or cleared, a Quote deleted. They fail the way every other change does.
/// </summary>
public sealed class GatewayLibraryClientQuoteTests
{
    private static readonly Guid EntryId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid QuoteId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    [Fact]
    public async Task Lists_a_books_quotes_as_library_ordered_them()
    {
        var (client, gateway) = CreateClient();
        gateway.Respond = _ => StubHttpMessageHandler.Json(
            $"[{Quote("a", 5, chapter: "Chapter 4")},{Quote("b", 23, note: "Mine.")}]");

        var outcome = await client.GetQuotesAsync(EntryId, CancellationToken.None);

        Assert.True(outcome.Ok);
        Assert.Equal(HttpMethod.Get, gateway.LastRequest!.Method);
        Assert.Equal($"/api/library/{EntryId}/quotes", gateway.LastRequest.RequestUri!.AbsolutePath);
        Assert.Collection(
            outcome.Value!,
            first =>
            {
                Assert.Equal(5, first.Percent);
                Assert.Equal("Chapter 4", first.Chapter);
                Assert.Null(first.Note);
            },
            second =>
            {
                Assert.Equal(23, second.Percent);
                Assert.Null(second.Chapter);
                Assert.Equal("Mine.", second.Note);
            });
    }

    [Fact]
    public async Task Edits_a_quotes_note()
    {
        var (client, gateway) = CreateClient();
        string? body = null;

        gateway.RespondAsync = async (request, cancellationToken) =>
        {
            body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return StubHttpMessageHandler.Json(Quote("a", 5, note: "Dorothea, near the end."));
        };

        var outcome = await client.SetQuoteNoteAsync(EntryId, QuoteId, "Dorothea, near the end.", CancellationToken.None);

        Assert.True(outcome.Ok);
        Assert.Equal("Dorothea, near the end.", outcome.Value!.Note);
        Assert.Equal(HttpMethod.Patch, gateway.LastRequest!.Method);
        Assert.Equal($"/api/library/{EntryId}/quotes/{QuoteId}", gateway.LastRequest.RequestUri!.AbsolutePath);
        Assert.Contains("\"note\":\"Dorothea, near the end.\"", body);
    }

    [Fact]
    public async Task Clears_a_note_by_sending_none()
    {
        var (client, gateway) = CreateClient();
        string? body = null;

        gateway.RespondAsync = async (request, cancellationToken) =>
        {
            body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return StubHttpMessageHandler.Json(Quote("a", 5));
        };

        var outcome = await client.SetQuoteNoteAsync(EntryId, QuoteId, null, CancellationToken.None);

        Assert.True(outcome.Ok);
        Assert.Null(outcome.Value!.Note);
        Assert.Contains("\"note\":null", body);
    }

    [Fact]
    public async Task Deletes_a_quote()
    {
        var (client, gateway) = CreateClient();
        gateway.Respond = _ => new HttpResponseMessage(HttpStatusCode.NoContent);

        var outcome = await client.DeleteQuoteAsync(EntryId, QuoteId, CancellationToken.None);

        Assert.True(outcome.Ok);
        Assert.Equal(HttpMethod.Delete, gateway.LastRequest!.Method);
        Assert.Equal($"/api/library/{EntryId}/quotes/{QuoteId}", gateway.LastRequest.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task Reports_a_quote_that_has_gone()
    {
        var (client, gateway) = CreateClient();
        gateway.Respond = _ => new HttpResponseMessage(HttpStatusCode.NotFound);

        var outcome = await client.DeleteQuoteAsync(EntryId, QuoteId, CancellationToken.None);

        Assert.Equal(LibraryChangeProblem.NoLongerThere, outcome.Problem);
    }

    [Fact]
    public async Task Reports_the_gateway_out_of_reach_when_listing()
    {
        var (client, gateway) = CreateClient();
        gateway.Respond = _ => throw new HttpRequestException("down");

        var outcome = await client.GetQuotesAsync(EntryId, CancellationToken.None);

        Assert.Equal(LibraryChangeProblem.GatewayUnreachable, outcome.Problem);
    }

    private static string Quote(string highlightId, decimal percent, string? note = null, string? chapter = null) =>
        $$"""
        {"id":"{{Guid.NewGuid()}}","highlightId":"{{highlightId}}","passage":"Words.","note":{{Json(note)}},"percent":{{percent}},"chapter":{{Json(chapter)}},"savedAt":"2026-10-02T18:40:00+00:00"}
        """;

    private static string Json(string? value) => value is null ? "null" : $"\"{value}\"";

    private static (GatewayLibraryClient Client, StubHttpMessageHandler Gateway) CreateClient()
    {
        var gateway = new StubHttpMessageHandler();
        var httpClient = new HttpClient(gateway) { BaseAddress = new Uri("http://gateway.test/") };
        return (new GatewayLibraryClient(httpClient, new ShelfChanges()), gateway);
    }
}
