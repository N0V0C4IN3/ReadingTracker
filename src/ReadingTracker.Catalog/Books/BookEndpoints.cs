using Microsoft.AspNetCore.Mvc;

namespace ReadingTracker.Catalog.Books;

public static class BookEndpoints
{
    /// <summary>
    /// Enough for any one page of a reader's library, while stopping a single request from
    /// asking for the whole table.
    /// </summary>
    private const int MaxBooksPerLookup = 200;

    public static void MapBookEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // Lets a caller render a list of Books without one request per Book.
        endpoints.MapGet("/api/books", async (
            [FromQuery] Guid[]? ids,
            BookCatalog catalog,
            CancellationToken cancellationToken) =>
        {
            if (ids is null || ids.Length == 0)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["ids"] = ["Supply at least one book id."],
                });
            }

            if (ids.Length > MaxBooksPerLookup)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["ids"] = [$"Ask for at most {MaxBooksPerLookup} books at a time."],
                });
            }

            var books = await catalog.FindAllAsync(ids, cancellationToken);

            // Ids matching nothing are simply absent: a caller rendering a list should not
            // lose every Book because one id went stale.
            return Results.Ok(books.Select(BookResponse.From));
        })
        .WithName("GetBooks");

        endpoints.MapGet("/api/books/search", async (
            string? isbn,
            string? title,
            string? author,
            int? page,
            int? pageSize,
            BookCatalog catalog,
            CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(isbn)
                && string.IsNullOrWhiteSpace(title)
                && string.IsNullOrWhiteSpace(author))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["query"] = ["Search by isbn, or by title and/or author."],
                });
            }

            if (PagingErrors(page, pageSize) is { Count: > 0 } pagingErrors)
            {
                return Results.ValidationProblem(pagingErrors);
            }

            var window = new SearchWindow(page ?? 1, pageSize ?? SearchWindow.DefaultPageSize);

            // An ISBN names one edition exactly, so it wins over the fuzzier fields — and there
            // is only ever one page of one edition, whatever window was asked for.
            var search = string.IsNullOrWhiteSpace(isbn)
                ? await catalog.SearchByTitleAndAuthorAsync(title, author, window, cancellationToken)
                : await catalog.SearchByIsbnAsync(isbn.Trim(), cancellationToken);

            return search.Status switch
            {
                // Saying "no results" here would tell the reader the book doesn't exist,
                // when the truth is that we could not find out.
                SearchStatus.ProvidersUnavailable => Results.Problem(
                    title: "Book search is temporarily unavailable",
                    detail: "No book data provider could be reached. This does not mean the book does not exist.",
                    statusCode: StatusCodes.Status503ServiceUnavailable),

                _ => Results.Ok(new BookSearchResponse(
                    [.. search.Books.Select(BookResponse.From)],
                    window.Page,
                    window.PageSize,
                    search.HasMore)),
            };
        })
        .WithName("SearchBooks");

        endpoints.MapPost("/api/books", async (
            NewBookRequest request,
            BookCatalog catalog,
            CancellationToken cancellationToken) =>
        {
            if (Validate(request) is { Count: > 0 } errors)
            {
                return Results.ValidationProblem(errors);
            }

            var book = await catalog.AddByHandAsync(
                request.Title!,
                request.Authors!,
                string.IsNullOrWhiteSpace(request.Isbn) ? null : request.Isbn.Trim(),
                string.IsNullOrWhiteSpace(request.CoverUrl) ? null : request.CoverUrl.Trim(),
                request.TotalPages,
                cancellationToken);

            return book is null
                ? Results.Problem(
                    title: "That ISBN is already in the catalog",
                    detail: "A Book already exists for this ISBN. Search for it instead of adding it again.",
                    statusCode: StatusCodes.Status409Conflict)
                : Results.Created($"/api/books/{book.Id}", BookResponse.From(book));
        })
        .WithName("AddBookByHand");

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

    /// <summary>
    /// A page that cannot exist is a mistake in the request, not an empty result: answering it
    /// with no matches would read as "there are no more books" rather than "you asked for
    /// page zero".
    /// </summary>
    private static Dictionary<string, string[]> PagingErrors(int? page, int? pageSize)
    {
        var errors = new Dictionary<string, string[]>();

        if (page is < 1)
        {
            errors[nameof(page)] = ["Pages start at 1."];
        }

        if (pageSize is < 1 or > SearchWindow.MaxPageSize)
        {
            errors[nameof(pageSize)] = [$"Ask for between 1 and {SearchWindow.MaxPageSize} results a page."];
        }

        return errors;
    }

    /// <summary>
    /// A Book's details as typed in by hand. Title and author are the minimum that makes a
    /// Book meaningful; everything else is what the reader happens to know.
    /// </summary>
    private static Dictionary<string, string[]> Validate(NewBookRequest request)
    {
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(request.Title))
        {
            errors[nameof(request.Title)] = ["A title is required."];
        }

        if (request.Authors is null || request.Authors.Count == 0 ||
            request.Authors.All(string.IsNullOrWhiteSpace))
        {
            errors[nameof(request.Authors)] = ["At least one author is required."];
        }

        if (request.TotalPages is <= 0)
        {
            errors[nameof(request.TotalPages)] = ["A page count must be greater than zero."];
        }

        return errors;
    }

    private sealed record NewBookRequest(
        string? Title,
        IReadOnlyList<string>? Authors,
        string? Isbn,
        string? CoverUrl,
        int? TotalPages);

    /// <summary>
    /// A page of matches. There is no total: Google Books' own count is an estimate that moves
    /// between requests and Open Library counts works rather than the editions it lists, so
    /// <paramref name="HasMore"/> is the most that can honestly be said about what follows.
    /// </summary>
    private sealed record BookSearchResponse(
        IReadOnlyList<BookResponse> Results,
        int Page,
        int PageSize,
        bool HasMore);

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
