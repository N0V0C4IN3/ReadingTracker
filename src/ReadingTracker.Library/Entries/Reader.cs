namespace ReadingTracker.Library.Entries;

/// <summary>
/// Who is asking. The Gateway verifies the caller's Google token and passes the reader on in
/// this header (ADR-0001, ADR-0007); Library never talks to Google itself.
///
/// This is only safe because Library sits behind the Gateway. Anything that can reach Library
/// directly could claim to be any reader, so Library must not be publicly routable.
/// </summary>
public static class Reader
{
    public const string HeaderName = "X-Reader-Id";

    /// <summary>Returns null when the caller did not say who they are.</summary>
    public static string? From(HttpRequest request) =>
        request.Headers.TryGetValue(HeaderName, out var values)
        && values.Count > 0
        && !string.IsNullOrWhiteSpace(values[0])
            ? values[0]!.Trim()
            : null;
}
