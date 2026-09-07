namespace ReadingTracker.Catalog.Books;

public static class BookEndpoints
{
    public static void MapBookEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/books/search", async (
            string isbn,
            BookCatalog catalog,
            CancellationToken cancellationToken) =>
        {
            var books = await catalog.SearchByIsbnAsync(isbn, cancellationToken);

            return Results.Ok(new BookSearchResponse([.. books.Select(BookResponse.From)]));
        })
        .WithName("SearchBooks");

        endpoints.MapGet("/api/books/{bookId:guid}", async (
            Guid bookId,
            BookCatalog catalog,
            CancellationToken cancellationToken) =>
        {
            var book = await catalog.FindAsync(bookId, cancellationToken);

            return book is null ? Results.NotFound() : Results.Ok(BookResponse.From(book));
        })
        .WithName("GetBook");
    }

    private sealed record BookSearchResponse(IReadOnlyList<BookResponse> Results);

    private sealed record BookResponse(
        Guid Id,
        string Title,
        IReadOnlyList<string> Authors,
        string? Isbn,
        string? CoverUrl,
        int? TotalPages)
    {
        public static BookResponse From(Book book) =>
            new(book.Id, book.Title, book.Authors, book.Isbn, book.CoverUrl, book.TotalPages);
    }
}
