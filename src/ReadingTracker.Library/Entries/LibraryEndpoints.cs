using ReadingTracker.Library.Catalog;

namespace ReadingTracker.Library.Entries;

public static class LibraryEndpoints
{
    public static void MapLibraryEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/library", async (
            HttpRequest request,
            ReadingLibrary library,
            CancellationToken cancellationToken) =>
        {
            if (Reader.From(request) is not { } readerId)
            {
                return NotSaidWhoIsAsking();
            }

            var entries = await library.ListAsync(readerId, cancellationToken);

            return Results.Ok(entries.Select(LibraryEntryResponse.From));
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
            if (await catalog.FindBookAsync(body.BookId, cancellationToken) is null)
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
        DateTimeOffset AddedAt)
    {
        public static LibraryEntryResponse From(LibraryEntry entry) =>
            new(entry.Id, entry.BookId, entry.Status.ToString(), entry.TrackingMethod.ToString(), entry.AddedAt);
    }
}
