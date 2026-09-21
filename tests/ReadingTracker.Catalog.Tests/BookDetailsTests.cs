using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace ReadingTracker.Catalog.Tests;

/// <summary>
/// The longer details of a Book — description, publisher, date, categories — which the single
/// lookup answers with and the list lookups leave out. A Google Books search carries them; an
/// Open Library one does not, so those Books are asked about once, from their provider, the
/// first time somebody looks.
/// </summary>
[Collection(CatalogApiCollection.Name)]
public sealed class BookDetailsTests(CatalogApiFixture fixture)
{
    private const string GoogleNoMatches = """{ "kind": "books#volumes", "totalItems": 0 }""";

    private static string GoogleVolumes(string isbn, string volumeId) => $$"""
        {
          "kind": "books#volumes",
          "totalItems": 1,
          "items": [
            {
              "id": "{{volumeId}}",
              "volumeInfo": {
                "title": "Red Rising",
                "authors": ["Pierce Brown"],
                "industryIdentifiers": [{ "type": "ISBN_13", "identifier": "{{isbn}}" }],
                "pageCount": 382,
                "publisher": "Del Rey",
                "publishedDate": "2014-01-28",
                "description": "<p>Darrow is a Red, a member of the lowest caste.</p><p>He works <b>all day</b> &amp; believes.<br>Yet he spends his life willingly.</p>",
                "categories": ["Fiction / General", "Fiction / Science Fiction / Space Opera", "Fiction / Dystopian", "fiction"]
              }
            }
          ]
        }
        """;

    private static string OpenLibraryIsbnMatch(string isbn, string editionKey) => $$"""
        {
          "ISBN:{{isbn}}": {
            "key": "{{editionKey}}",
            "title": "The Word for World Is Forest",
            "authors": [{ "name": "Ursula K. Le Guin" }],
            "number_of_pages": 189
          }
        }
        """;

    private const string OpenLibraryEdition = """
        {
          "key": "/books/OL17952222M",
          "title": "The Word for World Is Forest",
          "publishers": ["Tor Books"],
          "publish_date": "2010",
          "works": [{ "key": "/works/OL59870W" }]
        }
        """;

    private const string OpenLibraryWork = """
        {
          "key": "/works/OL59870W",
          "description": { "type": "/type/text", "value": "Centuries in the future, Terra has colonised Athshe.\r\n\r\nThe Athsheans fight back." },
          "subjects": ["Science fiction", "Colonization", "Forests", "Fiction", "science fiction", "War", "Ecology", "Dreams", "Athshe (Le Guin, Ursula K.)", "Anthropology", "Colonies"]
        }
        """;

    [Fact]
    public async Task Answers_with_the_details_a_google_books_search_already_carried_without_asking_again()
    {
        const string isbn = "9780345539786";
        fixture.GoogleBooks.Respond = _ => StubHttpMessageHandler.Json(GoogleVolumes(isbn, "redrising-1"));
        var client = fixture.CreateClient();

        var found = Assert.Single((await client.GetFromJsonAsync<SearchResponse>($"/api/books/search?isbn={isbn}"))!.Results);
        var callsAfterSearch = fixture.GoogleBooks.RequestCount;

        var book = await client.GetFromJsonAsync<BookDetail>($"/api/books/{found.Id}");

        Assert.Equal(callsAfterSearch, fixture.GoogleBooks.RequestCount);
        // Paragraphs kept, a lone line break made a space, markup gone, entities decoded.
        Assert.Equal(
            "Darrow is a Red, a member of the lowest caste.\n\nHe works all day & believes. Yet he spends his life willingly.",
            book!.Description);
        Assert.Equal("Del Rey", book.Publisher);
        Assert.Equal("2014-01-28", book.PublishedDate);
        // Paths split into names, "General" left out, repeats dropped whatever their case, provider's order kept.
        Assert.Equal(["Fiction", "Science Fiction", "Space Opera", "Dystopian"], book.Categories);
        Assert.Equal("https://books.google.com/books?id=redrising-1", book.ProviderUrl);
        Assert.False(book.DetailsUnavailable);
    }

    [Fact]
    public async Task Asks_open_library_once_for_a_book_it_found_and_keeps_what_came_back()
    {
        const string isbn = "9780765399991";
        fixture.GoogleBooks.Respond = _ => StubHttpMessageHandler.Json(GoogleNoMatches);
        fixture.OpenLibrary.Respond = request => request.RequestUri!.AbsolutePath switch
        {
            "/books/OL17952222M.json" => StubHttpMessageHandler.Json(OpenLibraryEdition),
            "/works/OL59870W.json" => StubHttpMessageHandler.Json(OpenLibraryWork),
            _ => StubHttpMessageHandler.Json(OpenLibraryIsbnMatch(isbn, "/books/OL17952222M")),
        };
        var client = fixture.CreateClient();

        var found = Assert.Single((await client.GetFromJsonAsync<SearchResponse>($"/api/books/search?isbn={isbn}"))!.Results);
        var callsAfterSearch = fixture.OpenLibrary.RequestCount;

        var book = await client.GetFromJsonAsync<BookDetail>($"/api/books/{found.Id}");

        // The edition, then the work it left the description and subjects to.
        Assert.Equal(callsAfterSearch + 2, fixture.OpenLibrary.RequestCount);
        Assert.Equal("Centuries in the future, Terra has colonised Athshe.\n\nThe Athsheans fight back.", book!.Description);
        Assert.Equal("Tor Books", book.Publisher);
        Assert.Equal("2010", book.PublishedDate);
        // Eleven subjects, one a repeat: the first eight distinct ones, a comma inside a name left alone.
        Assert.Equal(
            ["Science fiction", "Colonization", "Forests", "Fiction", "War", "Ecology", "Dreams", "Athshe (Le Guin, Ursula K.)"],
            book.Categories);
        Assert.Equal("https://openlibrary.org/books/OL17952222M", book.ProviderUrl);

        await client.GetFromJsonAsync<BookDetail>($"/api/books/{found.Id}");

        Assert.Equal(callsAfterSearch + 2, fixture.OpenLibrary.RequestCount);
    }

    [Fact]
    public async Task Records_that_it_looked_when_the_provider_no_longer_had_the_book()
    {
        const string isbn = "9780765324658";
        fixture.GoogleBooks.Respond = _ => StubHttpMessageHandler.Json(GoogleNoMatches);
        fixture.OpenLibrary.Respond = request => request.RequestUri!.AbsolutePath.EndsWith(".json")
            ? new HttpResponseMessage(HttpStatusCode.NotFound)
            : StubHttpMessageHandler.Json(OpenLibraryIsbnMatch(isbn, "/books/OL99999999M"));
        var client = fixture.CreateClient();

        var found = Assert.Single((await client.GetFromJsonAsync<SearchResponse>($"/api/books/search?isbn={isbn}"))!.Results);

        var first = await client.GetFromJsonAsync<BookDetail>($"/api/books/{found.Id}");
        var callsAfterFirstLook = fixture.OpenLibrary.RequestCount;
        var second = await client.GetFromJsonAsync<BookDetail>($"/api/books/{found.Id}");

        Assert.Null(first!.Description);
        Assert.Empty(first.Categories);
        Assert.False(first.DetailsUnavailable);
        Assert.Equal("The Word for World Is Forest", second!.Title);
        // Nothing is an answer too; the provider is not asked again for it.
        Assert.Equal(callsAfterFirstLook, fixture.OpenLibrary.RequestCount);
    }

    [Fact]
    public async Task Never_asks_a_provider_about_a_book_entered_by_hand()
    {
        var client = fixture.CreateClient();
        var created = await client.PostAsJsonAsync("/api/books", new
        {
            title = "My Grandmother's Recipes",
            authors = new[] { "Family" },
            totalPages = 40,
        });
        var byHand = (await created.Content.ReadFromJsonAsync<BookDetail>())!;
        var googleCalls = fixture.GoogleBooks.RequestCount;
        var openLibraryCalls = fixture.OpenLibrary.RequestCount;

        var book = await client.GetFromJsonAsync<BookDetail>($"/api/books/{byHand.Id}");

        Assert.Equal(googleCalls, fixture.GoogleBooks.RequestCount);
        Assert.Equal(openLibraryCalls, fixture.OpenLibrary.RequestCount);
        Assert.Null(book!.Description);
        Assert.Null(book.ProviderUrl);
        Assert.False(book.DetailsUnavailable);
    }

    [Fact]
    public async Task Tries_again_next_time_when_the_provider_could_not_be_reached()
    {
        const string isbn = "9780765324665";
        fixture.GoogleBooks.Respond = _ => StubHttpMessageHandler.Json(GoogleNoMatches);
        fixture.OpenLibrary.Respond = request => request.RequestUri!.AbsolutePath.EndsWith(".json")
            ? new HttpResponseMessage(HttpStatusCode.BadGateway)
            : StubHttpMessageHandler.Json(OpenLibraryIsbnMatch(isbn, "/books/OL17952222M"));
        var client = fixture.CreateClient();

        var found = Assert.Single((await client.GetFromJsonAsync<SearchResponse>($"/api/books/search?isbn={isbn}"))!.Results);

        var response = await client.GetAsync($"/api/books/{found.Id}");

        // The Book is still answered, as it is, and says why its details are missing.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var withoutDetails = (await response.Content.ReadFromJsonAsync<BookDetail>())!;
        Assert.Null(withoutDetails.Description);
        Assert.True(withoutDetails.DetailsUnavailable);

        // The provider is back.
        fixture.OpenLibrary.Respond = request => request.RequestUri!.AbsolutePath switch
        {
            "/books/OL17952222M.json" => StubHttpMessageHandler.Json(OpenLibraryEdition),
            "/works/OL59870W.json" => StubHttpMessageHandler.Json(OpenLibraryWork),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        };

        var withDetails = await client.GetFromJsonAsync<BookDetail>($"/api/books/{found.Id}");

        Assert.Equal("Tor Books", withDetails!.Publisher);
        Assert.False(withDetails.DetailsUnavailable);
    }

    [Fact]
    public async Task Leaves_the_details_out_of_the_list_lookup()
    {
        const string isbn = "9780345539793";
        fixture.GoogleBooks.Respond = _ => StubHttpMessageHandler.Json(GoogleVolumes(isbn, "redrising-2"));
        var client = fixture.CreateClient();

        var found = Assert.Single((await client.GetFromJsonAsync<SearchResponse>($"/api/books/search?isbn={isbn}"))!.Results);

        var listed = await client.GetFromJsonAsync<JsonElement>($"/api/books?ids={found.Id}");

        var book = Assert.Single(listed.EnumerateArray());
        Assert.Equal("Red Rising", book.GetProperty("title").GetString());
        Assert.False(book.TryGetProperty("description", out _));
        Assert.False(book.TryGetProperty("categories", out _));
    }

    private sealed record SearchResponse(IReadOnlyList<BookDetail> Results);

    private sealed record BookDetail(
        Guid Id,
        string Title,
        string? Description,
        string? Publisher,
        string? PublishedDate,
        IReadOnlyList<string> Categories,
        string? ProviderUrl,
        bool DetailsUnavailable);
}
