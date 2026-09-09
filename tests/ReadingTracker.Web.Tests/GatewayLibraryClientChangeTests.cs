using System.Net;
using ReadingTracker.Web.Services;

namespace ReadingTracker.Web.Tests;

/// <summary>
/// The operations that change something already on the shelf. They share one failure vocabulary,
/// so the point of most of these is that each HTTP answer becomes the outcome that tells the
/// reader something different — and, for a refusal, that Library's own wording survives the trip.
/// </summary>
public sealed class GatewayLibraryClientChangeTests
{
    private static readonly Guid EntryId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid SessionId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public async Task Sets_a_reading_status()
    {
        var (client, gateway) = CreateClient();
        string? body = null;

        gateway.RespondAsync = async (request, cancellationToken) =>
        {
            body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return StubHttpMessageHandler.Json(Entry("Reading"));
        };

        var outcome = await client.SetStatusAsync(EntryId, "Reading", CancellationToken.None);

        Assert.True(outcome.Ok);
        Assert.Equal("Reading", outcome.Value!.Status);
        Assert.Equal(HttpMethod.Put, gateway.LastRequest!.Method);
        Assert.Equal($"/api/library/{EntryId}/status", gateway.LastRequest.RequestUri!.AbsolutePath);
        Assert.Contains("\"status\":\"Reading\"", body);
    }

    [Fact]
    public async Task Repeats_what_library_said_when_it_refuses_a_change()
    {
        var (client, gateway) = CreateClient();

        // Library's own validation wording, which is already written for a person to read.
        gateway.Respond = _ => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent(
                """{"errors":{"session":["Say how much you read — more than nothing."]}}""",
                System.Text.Encoding.UTF8,
                "application/problem+json"),
        };

        var outcome = await client.LogSessionAsync(
            EntryId,
            new NewSession(0, null, null),
            CancellationToken.None);

        Assert.False(outcome.Ok);
        Assert.Equal(LibraryChangeProblem.Refused, outcome.Problem);
        Assert.Equal("Say how much you read — more than nothing.", Assert.Single(outcome.Reasons));
    }

    [Fact]
    public async Task Still_reports_a_refusal_whose_reason_cannot_be_read()
    {
        var (client, gateway) = CreateClient();

        gateway.Respond = _ => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("not json at all", System.Text.Encoding.UTF8, "text/plain"),
        };

        var outcome = await client.SetStatusAsync(EntryId, "Reading", CancellationToken.None);

        // The caller has its own general wording for this; what must not happen is the refusal
        // being lost, or an unreadable body throwing.
        Assert.Equal(LibraryChangeProblem.Refused, outcome.Problem);
        Assert.Empty(outcome.Reasons);
    }

    [Fact]
    public async Task Reports_something_that_is_no_longer_on_the_shelf()
    {
        var (client, gateway) = CreateClient();
        gateway.Respond = _ => new HttpResponseMessage(HttpStatusCode.NotFound);

        var outcome = await client.SetStatusAsync(EntryId, "Reading", CancellationToken.None);

        Assert.Equal(LibraryChangeProblem.NoLongerThere, outcome.Problem);
    }

    [Fact]
    public async Task Reports_when_the_session_has_ended()
    {
        var (client, gateway) = CreateClient();
        gateway.Respond = _ => new HttpResponseMessage(HttpStatusCode.Unauthorized);

        var outcome = await client.SetStatusAsync(EntryId, "Reading", CancellationToken.None);

        Assert.Equal(LibraryChangeProblem.NotSignedIn, outcome.Problem);
    }

    [Fact]
    public async Task Reports_when_the_gateway_cannot_be_reached_at_all()
    {
        var (client, gateway) = CreateClient();
        gateway.Respond = _ => throw new HttpRequestException("connection refused");

        var outcome = await client.SetStatusAsync(EntryId, "Reading", CancellationToken.None);

        Assert.Equal(LibraryChangeProblem.GatewayUnreachable, outcome.Problem);
    }

    [Fact]
    public async Task Sets_a_tracking_method()
    {
        var (client, gateway) = CreateClient();
        string? body = null;

        gateway.RespondAsync = async (request, cancellationToken) =>
        {
            body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return StubHttpMessageHandler.Json(Entry("Reading", trackingMethod: "Percentage"));
        };

        var outcome = await client.SetTrackingMethodAsync(EntryId, "Percentage", CancellationToken.None);

        Assert.True(outcome.Ok);
        Assert.Equal($"/api/library/{EntryId}/tracking-method", gateway.LastRequest!.RequestUri!.AbsolutePath);
        Assert.Contains("\"trackingMethod\":\"Percentage\"", body);
    }

    [Fact]
    public async Task Sets_the_readers_own_page_count()
    {
        var (client, gateway) = CreateClient();
        string? body = null;

        gateway.RespondAsync = async (request, cancellationToken) =>
        {
            body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return StubHttpMessageHandler.Json(Entry("Reading"));
        };

        await client.SetPageCountAsync(EntryId, 512, CancellationToken.None);

        Assert.Contains("\"totalPages\":512", body);
    }

    [Fact]
    public async Task Clears_the_page_count_by_sending_null_rather_than_omitting_it()
    {
        var (client, gateway) = CreateClient();
        string? body = null;

        gateway.RespondAsync = async (request, cancellationToken) =>
        {
            body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return StubHttpMessageHandler.Json(Entry("Reading"));
        };

        await client.SetPageCountAsync(EntryId, null, CancellationToken.None);

        // Library reads a null totalPages as "go back to the catalog's count". An omitted field
        // would deserialise to the same null, but sending it explicitly is what the contract
        // documents, and it survives any future change to how absent fields are treated.
        Assert.Contains("\"totalPages\":null", body);
    }

    [Fact]
    public async Task Logs_a_session_as_the_reader_described_it()
    {
        var (client, gateway) = CreateClient();
        string? body = null;

        gateway.RespondAsync = async (request, cancellationToken) =>
        {
            body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return StubHttpMessageHandler.Json(Session());
        };

        var when = new DateTimeOffset(2026, 9, 8, 0, 0, 0, TimeSpan.FromHours(3));
        var outcome = await client.LogSessionAsync(EntryId, new NewSession(22, when, 45), CancellationToken.None);

        Assert.True(outcome.Ok);
        Assert.Equal(HttpMethod.Post, gateway.LastRequest!.Method);
        Assert.Equal($"/api/library/{EntryId}/sessions", gateway.LastRequest.RequestUri!.AbsolutePath);
        Assert.Contains("\"amount\":22", body);
        Assert.Contains("\"durationMinutes\":45", body);
    }

    [Fact]
    public async Task Reads_a_session_back_with_the_amount_in_the_readers_current_method()
    {
        var (client, gateway) = CreateClient();
        gateway.Respond = _ => StubHttpMessageHandler.Json($"[{Session()}]");

        var outcome = await client.GetSessionsAsync(EntryId, CancellationToken.None);

        Assert.True(outcome.Ok);
        var session = Assert.Single(outcome.Value!);

        // Logged in pages, displayed as the percentage the reader now tracks by. Both travel,
        // because the recorded values are the record of what the reader actually entered.
        Assert.Equal(22, session.Amount);
        Assert.Equal("Pages", session.Unit);
        Assert.Equal(5, session.Displayed!.Amount);
        Assert.Equal("Percentage", session.Displayed.Unit);
    }

    [Fact]
    public async Task Corrects_a_session()
    {
        var (client, gateway) = CreateClient();
        gateway.Respond = _ => StubHttpMessageHandler.Json(Session());

        var outcome = await client.CorrectSessionAsync(
            EntryId,
            SessionId,
            new NewSession(30, null, null),
            CancellationToken.None);

        Assert.True(outcome.Ok);
        Assert.Equal(HttpMethod.Put, gateway.LastRequest!.Method);
        Assert.Equal($"/api/library/{EntryId}/sessions/{SessionId}", gateway.LastRequest.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task Deletes_a_session()
    {
        var (client, gateway) = CreateClient();
        gateway.Respond = _ => new HttpResponseMessage(HttpStatusCode.NoContent);

        var outcome = await client.DeleteSessionAsync(EntryId, SessionId, CancellationToken.None);

        Assert.True(outcome.Ok);
        Assert.Equal(HttpMethod.Delete, gateway.LastRequest!.Method);
        Assert.Equal($"/api/library/{EntryId}/sessions/{SessionId}", gateway.LastRequest.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task Removes_a_book_from_the_shelf()
    {
        var (client, gateway) = CreateClient();
        gateway.Respond = _ => new HttpResponseMessage(HttpStatusCode.NoContent);

        var outcome = await client.RemoveAsync(EntryId, CancellationToken.None);

        Assert.True(outcome.Ok);
        Assert.Equal(HttpMethod.Delete, gateway.LastRequest!.Method);
        Assert.Equal($"/api/library/{EntryId}", gateway.LastRequest.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task Reports_a_removal_that_had_already_happened()
    {
        var (client, gateway) = CreateClient();
        gateway.Respond = _ => new HttpResponseMessage(HttpStatusCode.NotFound);

        var outcome = await client.RemoveAsync(EntryId, CancellationToken.None);

        Assert.False(outcome.Ok);
        Assert.Equal(LibraryChangeProblem.NoLongerThere, outcome.Problem);
    }

    [Fact]
    public async Task Narrows_the_shelf_to_one_status()
    {
        var (client, gateway) = CreateClient();
        gateway.Respond = _ => StubHttpMessageHandler.Json("[]");

        await client.GetEntriesAsync("WantToRead", CancellationToken.None);

        Assert.Contains("status=WantToRead", gateway.LastRequest!.RequestUri!.Query);
    }

    [Fact]
    public async Task Asks_for_the_whole_shelf_when_no_status_is_given()
    {
        var (client, gateway) = CreateClient();
        gateway.Respond = _ => StubHttpMessageHandler.Json("[]");

        await client.GetEntriesAsync(null, CancellationToken.None);

        Assert.Equal("", gateway.LastRequest!.RequestUri!.Query);
    }

    [Fact]
    public async Task Reads_the_page_count_the_reader_set_apart_from_the_catalogs()
    {
        var (client, gateway) = CreateClient();
        gateway.Respond = _ => StubHttpMessageHandler.Json($"[{Entry("Reading", pageCountOverride: 512)}]");

        var (entries, _) = await client.GetEntriesAsync(null, CancellationToken.None);
        var entry = Assert.Single(entries!);

        Assert.Equal(512, entry.PageCountOverride);

        // Library works out which count applies and sends it, so nothing here has to decide.
        Assert.Equal(512, entry.EffectivePageCount);
        Assert.Equal(445, entry.Book!.TotalPages);
    }

    private static (GatewayLibraryClient Client, StubHttpMessageHandler Gateway) CreateClient()
    {
        var gateway = new StubHttpMessageHandler();
        var httpClient = new HttpClient(gateway) { BaseAddress = new Uri("http://gateway.test/") };
        return (new GatewayLibraryClient(httpClient), gateway);
    }

    private static string Entry(
        string status,
        string trackingMethod = "Pages",
        int? pageCountOverride = null) =>
        $$"""
        {
          "id": "{{EntryId}}",
          "bookId": "33333333-3333-3333-3333-333333333333",
          "status": "{{status}}",
          "trackingMethod": "{{trackingMethod}}",
          "addedAt": "2026-09-08T10:00:00+00:00",
          "pageCountOverride": {{(pageCountOverride?.ToString() ?? "null")}},
          "effectivePageCount": {{(pageCountOverride?.ToString() ?? "445")}},
          "book": {
            "title": "Dune",
            "authors": ["Frank Herbert"],
            "isbn": "9780441013593",
            "coverUrl": null,
            "totalPages": 445
          },
          "progress": null
        }
        """;

    private static string Session() =>
        $$"""
        {
          "id": "{{SessionId}}",
          "amount": 22,
          "unit": "Pages",
          "occurredAt": "2026-09-08T00:00:00+00:00",
          "durationMinutes": 45,
          "displayed": { "amount": 5, "unit": "Percentage" }
        }
        """;
}
