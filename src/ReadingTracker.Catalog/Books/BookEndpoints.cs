namespace ReadingTracker.Catalog.Books;

public static class BookEndpoints
{
    public static void MapBookEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/books/search", async (
            string isbn,
            IBookProvider provider,
            CancellationToken cancellationToken) =>
        {
            var results = await provider.SearchByIsbnAsync(isbn, cancellationToken);

            return Results.Ok(new BookSearchResponse(results));
        })
        .WithName("SearchBooks");
    }

    private sealed record BookSearchResponse(IReadOnlyList<BookSearchResult> Results);
}
