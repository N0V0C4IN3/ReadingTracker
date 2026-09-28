namespace ReadingTracker.Library.Entries;

/// <summary>The answers every one of Library's endpoints may give, whichever file maps it.</summary>
internal static class LibraryProblems
{
    public static IResult NotSaidWhoIsAsking() =>
        Results.Problem(
            title: "Unknown reader",
            detail: $"The {Reader.HeaderName} header is missing. Requests reach this service through the Gateway.",
            statusCode: StatusCodes.Status401Unauthorized);
}
