using Microsoft.AspNetCore.Mvc;

namespace ReadingTracker.Catalog.Books;

public static class BookEndpoints
{
    /// <summary>
    /// Enough for any one page of a reader's library, while stopping a single request from
    /// asking for the whole table.
    /// </summary>
    private const int MaxBooksPerLookup = 200;

    /// <summary>
    /// What a Book typed in by hand may be made of. Anyone signed in can add one, and one with
    /// an ISBN is shown to every reader who searches for it, so these are the Catalog's limits
    /// rather than the form's. Generous for any real book; there for the ones that are not.
    /// </summary>
    private const int MaxTitleLength = 200;

    private const int MaxAuthorLength = 200;

    private const int MaxAuthors = 10;

    private const int MaxCoverUrlLength = 2000;

    /// <summary>Longer than any book in print; a page count past this is not a page count.</summary>
    private const int MaxTotalPages = 20_000;

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
            string? q,
            string? isbn,
            string? title,
            string? author,
            int? page,
            int? pageSize,
            BookCatalog catalog,
            CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(q)
                && string.IsNullOrWhiteSpace(isbn)
                && string.IsNullOrWhiteSpace(title)
                && string.IsNullOrWhiteSpace(author))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["query"] = ["Search by q, by isbn, or by title and/or author."],
                });
            }

            // One box on the reader's side: whatever was typed into it is an ISBN if it is
            // shaped like one, and words to search for otherwise. Named fields still work.
            if (!string.IsNullOrWhiteSpace(q) && Isbn.TryNormalise(q, out var typedIsbn))
            {
                isbn = typedIsbn;
            }

            if (SearchErrors(q, isbn, title, author, page, pageSize) is { Count: > 0 } searchErrors)
            {
                return Results.ValidationProblem(searchErrors);
            }

            var window = new SearchWindow(page ?? 1, pageSize ?? SearchWindow.DefaultPageSize);

            // An ISBN names one edition exactly, so it wins over the fuzzier fields — and there
            // is only ever one page of one edition, whatever window was asked for.
            var search = !string.IsNullOrWhiteSpace(isbn)
                ? await catalog.SearchByIsbnAsync(isbn.Trim(), cancellationToken)
                : !string.IsNullOrWhiteSpace(q)
                    ? await catalog.SearchAsync(q, window, cancellationToken)
                    : await catalog.SearchByTitleAndAuthorAsync(title, author, window, cancellationToken);

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

            // The ISBN is stored the way an ISBN search looks one up: bare digits, no hyphens.
            // Validate has already established that it has that shape.
            var book = await catalog.AddByHandAsync(
                request.Title!,
                request.Authors!,
                Isbn.TryNormalise(request.Isbn, out var isbn) ? isbn : null,
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

        // One Book in full — the longer details included, which the list and search
        // responses leave out so that a shelf of two hundred books stays the size it is.
        endpoints.MapGet("/api/books/{bookId:guid}", async (
            Guid bookId,
            BookCatalog catalog,
            CancellationToken cancellationToken) =>
        {
            var lookup = await catalog.FindAsync(bookId, cancellationToken);

            return lookup is null ? Results.NotFound() : Results.Ok(BookDetailResponse.From(lookup));
        })
        .WithName("GetBook");
    }

    /// <summary>
    /// A search that cannot be run is a mistake in the request, not an empty result: answering
    /// a page that cannot exist with no matches would read as "there are no more books" rather
    /// than "you asked for page zero". Refused before any provider is asked.
    /// </summary>
    private static Dictionary<string, string[]> SearchErrors(
        string? q,
        string? isbn,
        string? title,
        string? author,
        int? page,
        int? pageSize)
    {
        var errors = new Dictionary<string, string[]>();

        // The same line for every field a reader can type into: none is allowed to be longer
        // than the longest thing it could name.
        foreach (var (field, value) in new[] { (nameof(q), q), (nameof(isbn), isbn), (nameof(title), title), (nameof(author), author) })
        {
            if (value is { } typed && typed.Trim().Length > SearchWindow.MaxQueryLength)
            {
                errors[field] = [$"A search can be at most {SearchWindow.MaxQueryLength} characters."];
            }
        }

        if (page is < 1)
        {
            errors[nameof(page)] = ["Pages start at 1."];
        }
        else if (page is > SearchWindow.MaxPage)
        {
            errors[nameof(page)] = [$"There is nothing past page {SearchWindow.MaxPage}."];
        }

        if (pageSize is < 1 or > SearchWindow.MaxPageSize)
        {
            errors[nameof(pageSize)] = [$"Ask for between 1 and {SearchWindow.MaxPageSize} results a page."];
        }

        return errors;
    }

    /// <summary>
    /// A Book's details as typed in by hand. Title and author are the minimum that makes a
    /// Book meaningful; everything else is what the reader happens to know — within the limits
    /// above, each refusal naming the field it is about.
    /// </summary>
    private static Dictionary<string, string[]> Validate(NewBookRequest request)
    {
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(request.Title))
        {
            errors[nameof(request.Title)] = ["A title is required."];
        }
        else if (request.Title.Trim().Length > MaxTitleLength)
        {
            errors[nameof(request.Title)] = [$"A title can be at most {MaxTitleLength} characters."];
        }

        if (request.Authors is null || request.Authors.Count == 0 ||
            request.Authors.All(string.IsNullOrWhiteSpace))
        {
            errors[nameof(request.Authors)] = ["At least one author is required."];
        }
        else if (request.Authors.Count > MaxAuthors)
        {
            errors[nameof(request.Authors)] = [$"A book can have at most {MaxAuthors} authors here."];
        }
        else if (request.Authors.Any(author => author is { } named && named.Trim().Length > MaxAuthorLength))
        {
            errors[nameof(request.Authors)] = [$"An author's name can be at most {MaxAuthorLength} characters."];
        }

        // Optional, but not free-form: an ISBN that is not shaped like one would never be found
        // by an ISBN search, and would take that ISBN's one slot in the catalog for nothing.
        if (!string.IsNullOrWhiteSpace(request.Isbn) && !Isbn.TryNormalise(request.Isbn, out _))
        {
            errors[nameof(request.Isbn)] = ["An ISBN is ten or thirteen digits, with or without hyphens."];
        }

        // Every reader's browser will load this as an image, so it has to be an address a
        // browser can be sent to: absolute, and https, since the site is.
        if (!string.IsNullOrWhiteSpace(request.CoverUrl))
        {
            var cover = request.CoverUrl.Trim();

            if (cover.Length > MaxCoverUrlLength)
            {
                errors[nameof(request.CoverUrl)] = [$"A cover URL can be at most {MaxCoverUrlLength} characters."];
            }
            else if (!Uri.TryCreate(cover, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            {
                errors[nameof(request.CoverUrl)] = ["A cover URL must be a full https:// address."];
            }
        }

        if (request.TotalPages is <= 0)
        {
            errors[nameof(request.TotalPages)] = ["A page count must be greater than zero."];
        }
        else if (request.TotalPages is > MaxTotalPages)
        {
            errors[nameof(request.TotalPages)] = [$"A page count can be at most {MaxTotalPages}."];
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

    /// <summary>
    /// A Book with its longer details. <paramref name="DetailsUnavailable"/> says the details
    /// are missing because the provider could not be reached this time, which a page can say
    /// out loud; details missing because there are none are simply null and empty.
    /// </summary>
    private sealed record BookDetailResponse(
        Guid Id,
        string Title,
        IReadOnlyList<string> Authors,
        string? Isbn,
        string? CoverUrl,
        int? TotalPages,
        string Source,
        string? Description,
        string? Publisher,
        string? PublishedDate,
        IReadOnlyList<string> Categories,
        Uri? ProviderUrl,
        bool DetailsUnavailable)
    {
        public static BookDetailResponse From(BookLookup lookup) => new(
            lookup.Book.Id,
            lookup.Book.Title,
            lookup.Book.Authors,
            lookup.Book.Isbn,
            lookup.Book.CoverUrl,
            lookup.Book.TotalPages,
            lookup.Book.Source.ToString(),
            // Cleaned when it was stored, and again on the way out: a Book cached before the
            // cleaning learned something new is served as though it had been cached after.
            DescriptionText.Clean(lookup.Book.Description),
            lookup.Book.Publisher,
            lookup.Book.PublishedDate,
            lookup.Book.Categories,
            lookup.ProviderUrl,
            lookup.DetailsUnavailable);
    }
}
