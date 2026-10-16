using System.Net;
using System.Net.Http.Json;

namespace ReadingTracker.Library.Tests;

/// <summary>
/// A passage the Reader sent on purpose from their device, kept on the LibraryEntry. A device
/// save is keyed by the highlight's identity on the device, so sending it again is the same
/// Quote (ADR-0017).
/// </summary>
[Collection(LibraryApiCollection.Name)]
public sealed class QuoteTests(LibraryApiFixture fixture)
{
    private static readonly DateTimeOffset SavedAt = new(2026, 10, 12, 21, 40, 0, TimeSpan.FromHours(3));

    [Fact]
    public async Task Saving_a_passage_keeps_it_with_everything_the_device_said()
    {
        var client = fixture.ClientFor("first-quote-reader");
        var entryId = await fixture.AddBookAsync(client);

        var response = await SaveAsync(
            client,
            entryId,
            "1760294400-0042",
            passage: "It is never too late to be what you might have been.",
            note: "Pinned above the desk.",
            percent: 42.5m,
            chapter: "Book Eight",
            savedAt: SavedAt);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var saved = (await response.Content.ReadFromJsonAsync<Saved>())!;
        Assert.Equal("Created", saved.Outcome);
        Assert.NotEqual(Guid.Empty, saved.Quote.Id);
        Assert.Equal("1760294400-0042", saved.Quote.HighlightId);
        Assert.Equal("It is never too late to be what you might have been.", saved.Quote.Passage);
        Assert.Equal("Pinned above the desk.", saved.Quote.Note);
        Assert.Equal(42.5m, saved.Quote.Percent);
        Assert.Equal("Book Eight", saved.Quote.Chapter);
        Assert.Equal(SavedAt, saved.Quote.SavedAt);
        Assert.Equal($"/api/library/{entryId}/quotes/{saved.Quote.Id}", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task Saving_the_same_highlight_again_updates_its_note_and_nothing_else()
    {
        var client = fixture.ClientFor("resaving-reader");
        var entryId = await fixture.AddBookAsync(client);
        var first = await SaveAndReadAsync(
            client, entryId, "hl-7", passage: "The words as first sent.", note: "First thought.", percent: 30m, chapter: "One", savedAt: SavedAt);

        var again = await SaveAsync(
            client, entryId, "hl-7", passage: "The words, re-flowed.", note: "Second thought.", percent: 31m, chapter: "Two", savedAt: SavedAt.AddHours(1));

        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        var updated = (await again.Content.ReadFromJsonAsync<Saved>())!;
        Assert.Equal("Updated", updated.Outcome);
        // The same Quote, with the new note. The passage and where it is stand as first sent.
        Assert.Equal(first.Quote with { Note = "Second thought." }, updated.Quote);
        Assert.Equal(updated.Quote, Assert.Single(await QuotesAsync(client, entryId)));
    }

    [Fact]
    public async Task Saving_the_same_highlight_with_no_note_takes_the_note_away()
    {
        var client = fixture.ClientFor("unnoting-device-reader");
        var entryId = await fixture.AddBookAsync(client);
        await SaveAsync(client, entryId, "hl-noted", note: "Wrote this on the device.");

        var updated = (await (await SaveAsync(client, entryId, "hl-noted", note: null)).Content.ReadFromJsonAsync<Saved>())!;

        Assert.Equal("Updated", updated.Outcome);
        Assert.Null(updated.Quote.Note);
    }

    [Fact]
    public async Task A_retried_save_is_one_quote_not_two()
    {
        var client = fixture.ClientFor("retrying-reader");
        var entryId = await fixture.AddBookAsync(client);

        // The same request twice, as a device sends it again after a dropped connection.
        await SaveAsync(client, entryId, "hl-retried", passage: "Sent twice.", savedAt: SavedAt);
        var retried = await SaveAsync(client, entryId, "hl-retried", passage: "Sent twice.", savedAt: SavedAt);

        // The retry is answered, not refused: the device must be able to stop retrying.
        Assert.Equal(HttpStatusCode.OK, retried.StatusCode);
        Assert.Single(await QuotesAsync(client, entryId));
    }

    [Fact]
    public async Task The_same_words_highlighted_again_are_a_second_quote()
    {
        var client = fixture.ClientFor("re-reading-reader");
        var entryId = await fixture.AddBookAsync(client);
        await SaveAsync(client, entryId, "first-reading", passage: "Worth reading twice.", percent: 50m, savedAt: SavedAt);

        var second = await SaveAsync(client, entryId, "second-reading", passage: "Worth reading twice.", percent: 50m, savedAt: SavedAt.AddYears(1));

        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        Assert.Equal(["first-reading", "second-reading"], (await QuotesAsync(client, entryId)).Select(q => q.HighlightId));
    }

    [Fact]
    public async Task My_quotes_come_back_in_the_order_the_book_tells_them()
    {
        var client = fixture.ClientFor("book-order-reader");
        var entryId = await fixture.AddBookAsync(client);
        // Saved out of order: a passage near the end first, then two at the same place.
        await SaveAsync(client, entryId, "late", passage: "Near the end.", percent: 88m, savedAt: SavedAt);
        await SaveAsync(client, entryId, "second-look", passage: "Again, later.", percent: 12m, savedAt: SavedAt.AddDays(2));
        await SaveAsync(client, entryId, "first-look", passage: "Early on.", percent: 12m, savedAt: SavedAt.AddDays(1));
        await SaveAsync(client, entryId, "opening", passage: "The first line.", percent: 0m, savedAt: SavedAt.AddDays(3));

        var quotes = await QuotesAsync(client, entryId);

        // By where they are in the book, and two at the same place by when each was saved.
        Assert.Equal(["opening", "first-look", "second-look", "late"], quotes.Select(q => q.HighlightId));
    }

    [Fact]
    public async Task A_book_with_no_quotes_lists_none()
    {
        var client = fixture.ClientFor("no-quotes-reader");
        var entryId = await fixture.AddBookAsync(client);

        Assert.Empty(await QuotesAsync(client, entryId));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Refuses_a_quote_with_no_passage(string? passage)
    {
        var client = fixture.ClientFor($"no-passage-reader-{passage?.Length}");
        var entryId = await fixture.AddBookAsync(client);

        var response = await SaveAsync(client, entryId, "hl-empty", passage: passage);

        await AssertRefusedAsync(response, "passage");
        Assert.Empty(await QuotesAsync(client, entryId));
    }

    [Theory]
    [InlineData(-0.5)]
    [InlineData(100.5)]
    [InlineData(null)]
    public async Task Refuses_a_quote_that_is_not_somewhere_in_the_book(double? percent)
    {
        var client = fixture.ClientFor($"off-the-page-quoter-{percent}");
        var entryId = await fixture.AddBookAsync(client);

        var response = await SaveAsync(client, entryId, "hl-nowhere", percent: (decimal?)percent);

        await AssertRefusedAsync(response, "percent");
        Assert.Empty(await QuotesAsync(client, entryId));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(100)]
    public async Task Takes_a_quote_on_the_first_or_the_last_page(decimal percent)
    {
        var client = fixture.ClientFor($"edge-of-the-book-quoter-{percent}");
        var entryId = await fixture.AddBookAsync(client);

        var saved = await SaveAndReadAsync(client, entryId, "hl-edge", percent: percent);

        Assert.Equal(percent, saved.Quote.Percent);
    }

    [Fact]
    public async Task Refuses_a_highlight_identity_too_long_to_be_one()
    {
        var client = fixture.ClientFor("long-identity-reader");
        var entryId = await fixture.AddBookAsync(client);

        var response = await SaveAsync(client, entryId, new string('x', 201));

        await AssertRefusedAsync(response, "highlightId");
    }

    [Fact]
    public async Task Saving_to_a_book_not_on_my_shelf_is_not_found()
    {
        var response = await SaveAsync(fixture.ClientFor("shelfless-quoter"), Guid.NewGuid(), "hl-1");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task I_can_write_a_note_on_a_quote()
    {
        var client = fixture.ClientFor("noting-reader");
        var entryId = await fixture.AddBookAsync(client);
        var saved = (await SaveAndReadAsync(client, entryId, "hl-bare", passage: "Unremarked.", percent: 20m, chapter: "Two", savedAt: SavedAt)).Quote;

        var response = await NoteAsync(client, entryId, saved.Id, "Remarked on, now.");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var noted = (await response.Content.ReadFromJsonAsync<Quote>())!;
        Assert.Equal(saved with { Note = "Remarked on, now." }, noted);
        Assert.Equal(noted, Assert.Single(await QuotesAsync(client, entryId)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  \n ")]
    public async Task A_blank_note_takes_my_note_away(string? note)
    {
        var client = fixture.ClientFor($"unnoting-reader-{note?.Length}");
        var entryId = await fixture.AddBookAsync(client);
        var saved = (await SaveAndReadAsync(client, entryId, "hl-noted", note: "A second thought.")).Quote;

        var noted = (await (await NoteAsync(client, entryId, saved.Id, note)).Content.ReadFromJsonAsync<Quote>())!;

        Assert.Null(noted.Note);
        Assert.Null(Assert.Single(await QuotesAsync(client, entryId)).Note);
    }

    [Fact]
    public async Task The_passage_is_the_books_words_and_nothing_changes_it()
    {
        var client = fixture.ClientFor("rewording-reader");
        var entryId = await fixture.AddBookAsync(client);
        var saved = (await SaveAndReadAsync(client, entryId, "hl-words", passage: "As the author wrote it.", percent: 40m)).Quote;

        // Neither editing the note nor saving the highlight again can put other words in.
        await client.PatchAsJsonAsync(
            $"/api/library/{entryId}/quotes/{saved.Id}",
            new { note = "Mine.", passage = "As I would have put it.", percent = 90 });
        await SaveAsync(client, entryId, "hl-words", passage: "Something else entirely.", note: "Mine.", percent: 90m);

        var quote = Assert.Single(await QuotesAsync(client, entryId));
        Assert.Equal("As the author wrote it.", quote.Passage);
        Assert.Equal(40m, quote.Percent);
    }

    [Fact]
    public async Task Noting_a_quote_that_is_not_there_is_not_found()
    {
        var client = fixture.ClientFor("noting-nothing-reader");
        var entryId = await fixture.AddBookAsync(client);

        var response = await NoteAsync(client, entryId, Guid.NewGuid(), "To nobody.");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Noting_a_quote_through_another_of_my_books_is_not_found()
    {
        var client = fixture.ClientFor("crossed-books-reader");
        var oneBook = await fixture.AddBookAsync(client);
        var anotherBook = await fixture.AddBookAsync(client);
        var saved = (await SaveAndReadAsync(client, oneBook, "hl-here")).Quote;

        var response = await NoteAsync(client, anotherBook, saved.Id, "Wrong book.");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Null(Assert.Single(await QuotesAsync(client, oneBook)).Note);
    }

    [Fact]
    public async Task Another_reader_cannot_note_my_quote()
    {
        var owner = fixture.ClientFor("noted-quote-owner");
        var entryId = await fixture.AddBookAsync(owner);
        var saved = (await SaveAndReadAsync(owner, entryId, "hl-private")).Quote;

        var response = await NoteAsync(fixture.ClientFor("noted-quote-stranger"), entryId, saved.Id, "Not yours.");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Null(Assert.Single(await QuotesAsync(owner, entryId)).Note);
    }

    [Fact]
    public async Task Another_reader_can_neither_see_my_quotes_nor_add_to_them()
    {
        var owner = fixture.ClientFor("quote-owner");
        var stranger = fixture.ClientFor("quote-stranger");
        var entryId = await fixture.AddBookAsync(owner);
        await SaveAsync(owner, entryId, "hl-mine", passage: "Mine alone.");

        var looked = await stranger.GetAsync($"/api/library/{entryId}/quotes");
        var added = await SaveAsync(stranger, entryId, "hl-theirs");
        var overwritten = await SaveAsync(stranger, entryId, "hl-mine", note: "Not yours.");

        Assert.Equal(HttpStatusCode.NotFound, looked.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, added.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, overwritten.StatusCode);
        var mine = Assert.Single(await QuotesAsync(owner, entryId));
        Assert.Equal("hl-mine", mine.HighlightId);
        Assert.Null(mine.Note);
    }

    [Fact]
    public async Task Taking_the_book_off_my_shelf_takes_its_quotes_with_it()
    {
        var bookId = Guid.NewGuid();
        fixture.CatalogHasBook(bookId);
        var client = fixture.ClientFor("quote-removing-reader");
        var entryId = await AddAsync(client, bookId);
        await SaveAsync(client, entryId, "hl-gone");

        await client.DeleteAsync($"/api/library/{entryId}");
        var again = await AddAsync(client, bookId);

        // Nothing left pointing at the entry that went, and nothing carried over to the new one.
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/library/{entryId}/quotes")).StatusCode);
        Assert.Empty(await QuotesAsync(client, again));
    }

    private static Task<HttpResponseMessage> NoteAsync(HttpClient client, Guid entryId, Guid quoteId, string? note) =>
        client.PatchAsJsonAsync($"/api/library/{entryId}/quotes/{quoteId}", new { note });

    private static async Task<Guid> AddAsync(HttpClient client, Guid bookId) =>
        (await (await client.PostAsJsonAsync("/api/library", new { bookId })).Content.ReadFromJsonAsync<Added>())!.Id;

    private sealed record Added(Guid Id);

    private static async Task AssertRefusedAsync(HttpResponseMessage response, string field)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = (await response.Content.ReadFromJsonAsync<Problem>())!;
        Assert.Contains(field, problem.Errors.Keys);
    }

    private sealed record Problem(IReadOnlyDictionary<string, string[]> Errors);

    private static async Task<IReadOnlyList<Quote>> QuotesAsync(HttpClient client, Guid entryId)
    {
        var response = await client.GetAsync($"/api/library/{entryId}/quotes");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<IReadOnlyList<Quote>>())!;
    }

    private static async Task<Saved> SaveAndReadAsync(
        HttpClient client,
        Guid entryId,
        string highlightId,
        string? passage = "A passage worth keeping.",
        string? note = null,
        decimal? percent = 10m,
        string? chapter = null,
        DateTimeOffset? savedAt = null)
    {
        var response = await SaveAsync(client, entryId, highlightId, passage, note, percent, chapter, savedAt);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<Saved>())!;
    }

    private static Task<HttpResponseMessage> SaveAsync(
        HttpClient client,
        Guid entryId,
        string highlightId,
        string? passage = "A passage worth keeping.",
        string? note = null,
        decimal? percent = 10m,
        string? chapter = null,
        DateTimeOffset? savedAt = null) =>
        client.PutAsJsonAsync(
            $"/api/library/{entryId}/quotes/{Uri.EscapeDataString(highlightId)}",
            new { passage, note, percent, chapter, savedAt });

    private sealed record Saved(string Outcome, Quote Quote);

    private sealed record Quote(
        Guid Id,
        string HighlightId,
        string Passage,
        string? Note,
        decimal Percent,
        string? Chapter,
        DateTimeOffset SavedAt);
}
