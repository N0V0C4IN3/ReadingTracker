using System.Net;
using System.Text;
using System.Text.Json;
using ReadingTracker.Web.Services;

namespace ReadingTracker.Web.Tests;

/// <summary>
/// Adding a Book the Catalog has never heard of. Stubbed at the network boundary, so the real
/// request building and response reading run against what Catalog actually answers with.
/// </summary>
public sealed class GatewayCatalogClientCreateTests
{
    private static readonly NewBook ABookNobodyHas =
        new("A Book Nobody Has", ["Someone Unknown"], null, null, null);

    [Fact]
    public async Task Creates_the_book_and_hands_back_its_new_id()
    {
        var (client, gateway) = CreateClient();
        var bookId = Guid.NewGuid();
        gateway.Respond = _ => Created($$"""
            {
              "id": "{{bookId}}",
              "title": "A Book Nobody Has",
              "authors": ["Someone Unknown"],
              "isbn": null,
              "coverUrl": null,
              "totalPages": null,
              "source": "Manual"
            }
            """);

        var creation = await client.CreateAsync(ABookNobodyHas, CancellationToken.None);

        Assert.True(creation.Ok);
        Assert.Equal(bookId, creation.Book!.Id);
        Assert.Equal("A Book Nobody Has", creation.Book.Title);
        Assert.Equal(["Someone Unknown"], creation.Book.Authors);
    }

    [Fact]
    public async Task Sends_the_details_in_the_shape_the_catalog_asks_for()
    {
        var (client, gateway) = CreateClient();
        JsonElement? sent = null;
        gateway.RespondAsync = async (request, cancellationToken) =>
        {
            sent = JsonSerializer.Deserialize<JsonElement>(
                await request.Content!.ReadAsStringAsync(cancellationToken));

            return Created("""{ "id": "11111111-1111-1111-1111-111111111111", "title": "T", "authors": [] }""");
        };

        await client.CreateAsync(
            new NewBook("Earthsea", ["Ursula K. Le Guin", "A Second Author"], "9780380014422", null, 200),
            CancellationToken.None);

        var body = sent!.Value;
        Assert.Equal("Earthsea", body.GetProperty("title").GetString());
        // An array, not a joined string: Catalog takes authors one at a time.
        Assert.Equal(
            ["Ursula K. Le Guin", "A Second Author"],
            body.GetProperty("authors").EnumerateArray().Select(author => author.GetString()));
        Assert.Equal("9780380014422", body.GetProperty("isbn").GetString());
        Assert.Equal(200, body.GetProperty("totalPages").GetInt32());

        // Catalog null-checks its optional fields, so an unknown one is null rather than "".
        Assert.Equal(JsonValueKind.Null, body.GetProperty("coverUrl").ValueKind);
    }

    [Fact]
    public async Task Repeats_what_the_catalog_said_about_each_field_it_refused()
    {
        var (client, gateway) = CreateClient();
        gateway.Respond = _ => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent(
                """
                {
                  "title": "One or more validation errors occurred.",
                  "status": 400,
                  "errors": {
                    "Title": ["A title is required."],
                    "Authors": ["At least one author is required."]
                  }
                }
                """,
                Encoding.UTF8,
                "application/problem+json"),
        };

        var creation = await client.CreateAsync(new NewBook("", [], null, null, null), CancellationToken.None);

        Assert.Equal(BookCreationProblem.Rejected, creation.Problem);
        Assert.Equal(["A title is required."], creation.For("Title"));
        Assert.Equal(["At least one author is required."], creation.For("Authors"));

        // Named however the asking form knows the field, not only however Catalog cased it.
        Assert.Equal(["A title is required."], creation.For("title"));

        // A field Catalog said nothing about has nothing to show under it.
        Assert.Empty(creation.For("TotalPages"));
    }

    [Fact]
    public async Task Reports_when_the_isbn_is_already_in_the_catalog()
    {
        var (client, gateway) = CreateClient();
        gateway.Respond = _ => new HttpResponseMessage(HttpStatusCode.Conflict);

        var creation = await client.CreateAsync(
            ABookNobodyHas with { Isbn = "9780380014422" },
            CancellationToken.None);

        Assert.Null(creation.Book);
        Assert.Equal(BookCreationProblem.IsbnAlreadyInCatalog, creation.Problem);
    }

    [Fact]
    public async Task Reports_when_the_session_has_ended()
    {
        var (client, gateway) = CreateClient();
        gateway.Respond = _ => new HttpResponseMessage(HttpStatusCode.Unauthorized);

        var creation = await client.CreateAsync(ABookNobodyHas, CancellationToken.None);

        Assert.Null(creation.Book);
        Assert.Equal(BookCreationProblem.NotSignedIn, creation.Problem);
    }

    [Fact]
    public async Task Reports_when_the_gateway_cannot_be_reached_at_all()
    {
        var (client, gateway) = CreateClient();
        gateway.Respond = _ => throw new HttpRequestException("connection refused");

        var creation = await client.CreateAsync(ABookNobodyHas, CancellationToken.None);

        Assert.Null(creation.Book);
        Assert.Equal(BookCreationProblem.GatewayUnreachable, creation.Problem);
    }

    private static HttpResponseMessage Created(string body) =>
        new(HttpStatusCode.Created) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static (GatewayCatalogClient Client, StubHttpMessageHandler Gateway) CreateClient()
    {
        var gateway = new StubHttpMessageHandler();
        var httpClient = new HttpClient(gateway) { BaseAddress = new Uri("http://gateway.test/") };
        return (new GatewayCatalogClient(httpClient), gateway);
    }
}
