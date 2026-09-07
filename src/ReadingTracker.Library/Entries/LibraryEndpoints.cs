using ReadingTracker.Library.Catalog;

namespace ReadingTracker.Library.Entries;

public static class LibraryEndpoints
{
    public static void MapLibraryEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/library", async (
            HttpRequest request,
            ReadingLibrary library,
            CatalogClient catalog,
            CancellationToken cancellationToken) =>
        {
            if (Reader.From(request) is not { } readerId)
            {
                return NotSaidWhoIsAsking();
            }

            var entries = await library.ListAsync(readerId, cancellationToken);

            // One trip to Catalog for the whole shelf, not one per book. Best-effort: if
            // Catalog is unreachable the entries still come back, just without book details.
            var books = await catalog.TryFindBooksAsync(
                [.. entries.Select(entry => entry.BookId).Distinct()],
                cancellationToken);

            return Results.Ok(entries.Select(entry =>
                LibraryEntryResponse.From(entry, books.GetValueOrDefault(entry.BookId))));
        })
        .WithName("ListLibrary");

        endpoints.MapPost("/api/library", async (
            AddToLibraryRequest body,
            HttpRequest request,
            ReadingLibrary library,
            CatalogClient catalog,
            CancellationToken cancellationToken) =>
        {
            if (Reader.From(request) is not { } readerId)
            {
                return NotSaidWhoIsAsking();
            }

            // Confirm the Book exists before recording a relationship to it, so an entry can
            // never point at nothing.
            CatalogBook? book;

            try
            {
                book = await catalog.FindBookAsync(body.BookId, cancellationToken);
            }
            catch (HttpRequestException)
            {
                // Unlike listing, adding cannot degrade: without Catalog we cannot tell an
                // unknown book from an unreachable one, and guessing would create an entry
                // pointing at nothing.
                return Results.Problem(
                    title: "The catalog is temporarily unavailable",
                    detail: "The book could not be confirmed just now. Try again shortly.",
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            if (book is null)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    [nameof(body.BookId)] = ["No such book in the catalog."],
                });
            }

            var (entry, failure) = await library.AddAsync(readerId, body.BookId, cancellationToken);

            return failure switch
            {
                AddToLibraryFailure.AlreadyInLibrary => Results.Problem(
                    title: "That book is already in your library",
                    detail: "A book appears in a reader's library once.",
                    statusCode: StatusCodes.Status409Conflict),

                _ => Results.Created($"/api/library/{entry!.Id}", LibraryEntryResponse.From(entry)),
            };
        })
        .WithName("AddToLibrary");
    }

    private static IResult NotSaidWhoIsAsking() =>
        Results.Problem(
            title: "Unknown reader",
            detail: $"The {Reader.HeaderName} header is missing. Requests reach this service through the Gateway.",
            statusCode: StatusCodes.Status401Unauthorized);

    private sealed record AddToLibraryRequest(Guid BookId);

    private sealed record LibraryEntryResponse(
        Guid Id,
        Guid BookId,
        string Status,
        string TrackingMethod,
        DateTimeOffset AddedAt,
        BookDetailsResponse? Book)
    {
        public static LibraryEntryResponse From(LibraryEntry entry, CatalogBook? book = null) =>
            new(
                entry.Id,
                entry.BookId,
                entry.Status.ToString(),
                entry.TrackingMethod.ToString(),
                entry.AddedAt,
                book is null ? null : BookDetailsResponse.From(book));
    }

    /// <summary>
    /// Catalog's description of the book, passed through rather than stored, so it can never
    /// drift from what Catalog says. Absent when Catalog could not be reached.
    /// </summary>
    private sealed record BookDetailsResponse(
        string Title,
        IReadOnlyList<string> Authors,
        string? Isbn,
        string? CoverUrl,
        int? TotalPages)
    {
        public static BookDetailsResponse From(CatalogBook book) =>
            new(book.Title, book.Authors, book.Isbn, book.CoverUrl, book.TotalPages);
    }
}
