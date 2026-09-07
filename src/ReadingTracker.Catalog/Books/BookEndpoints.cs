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
            var search = await catalog.SearchByIsbnAsync(isbn, cancellationToken);

            return search.Status switch
            {
                // Saying "no results" here would tell the reader the book doesn't exist,
                // when the truth is that we could not find out.
                SearchStatus.ProvidersUnavailable => Results.Problem(
                    title: "Book search is temporarily unavailable",
                    detail: "No book data provider could be reached. This does not mean the book does not exist.",
                    statusCode: StatusCodes.Status503ServiceUnavailable),

                _ => Results.Ok(new BookSearchResponse([.. search.Books.Select(BookResponse.From)])),
            };
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
        int? TotalPages,
        string Source)
    {
        public static BookResponse From(Book book) =>
            new(book.Id, book.Title, book.Authors, book.Isbn, book.CoverUrl, book.TotalPages, book.Source.ToString());
    }
}
