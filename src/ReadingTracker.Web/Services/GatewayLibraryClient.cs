using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ReadingTracker.Web.Services;

/// <summary>A LibraryEntry as the Gateway (by way of Library) describes it.</summary>
/// <param name="Progress">Library's answer as it came; read through <see cref="AmountRead"/>, not field by field.</param>
/// <param name="Bookmark">Where a device last said the reader is; null when no device has said.</param>
public sealed record LibraryEntry(
    Guid Id,
    Guid BookId,
    string Status,
    string TrackingMethod,
    DateTimeOffset AddedAt,
    DateOnly? FinishedOn,
    int? PageCountOverride,
    int? EffectivePageCount,
    BookDetails? Book,
    Progress? Progress,
    Bookmark? Bookmark)
{
    /// <summary>How much of the book the reader has read, as a case a display can tell apart.</summary>
    [JsonIgnore]
    public AmountRead AmountRead => AmountRead.Of(Progress);
}

/// <summary>
/// Where the reader is in a book, as a percentage, as last reported by a device (ADR-0015). A
/// position, not a total: it can move backwards, and it is shown beside the progress rather than
/// as it. Read-only here — setting it by hand would make it a second source of truth for
/// progress, which is exactly what it is not.
/// </summary>
public sealed record Bookmark(decimal Percent, DateTimeOffset ReportedAt);

/// <summary>
/// A year's goal: how many books the reader means to finish, or null for none set, beside how
/// many they have finished in it — the count is there either way.
/// </summary>
public sealed record ReadingGoal(int Year, int? Books, int Finished);

public sealed record BookDetails(
    string Title,
    IReadOnlyList<string> Authors,
    string? Isbn,
    string? CoverUrl,
    int? TotalPages);

/// <summary>
/// How much of a book the reader has read, added up from their sessions. <paramref name="AmountRead"/>
/// and <paramref name="Unit"/> are null together, when the reader has logged in both pages and
/// percent and no page count exists to add the two together with. Those fields mean something
/// only together, so it is read through <see cref="Services.AmountRead"/>.
/// </summary>
public sealed record Progress(decimal? AmountRead, string? Unit, int? PercentComplete);

/// <summary>
/// A ReadingSession as it was recorded, plus <paramref name="Displayed"/> — the same reading in
/// whatever method the reader tracks the book by now. The recorded amount is never converted,
/// because it is the record of what the reader actually entered; Library sends both so a client
/// never has to work the conversion out for itself.
/// </summary>
/// <param name="Source">"Reader" for a session typed in, "Device" for one a device reported.</param>
/// <param name="Pages">
/// What it came to in pages, as Library worked it out from the book's effective page count:
/// unrounded, so a day's or a year's can be added up, and not stopped at the end of the book,
/// because a session is kept as it was stated. Null when it was logged in percent of a book
/// nobody knows the length of.
/// </param>
public sealed record ReadingSessionView(
    Guid Id,
    decimal Amount,
    string Unit,
    string Source,
    DateTimeOffset OccurredAt,
    int? DurationMinutes,
    DisplayedAmount? Displayed,
    decimal? Pages);

public sealed record DisplayedAmount(decimal Amount, string Unit);

/// <summary>A stretch of time's reading across the shelf: the books read in it and every sitting.</summary>
public sealed record ReadingSpan(IReadOnlyList<ReadBook> Books, IReadOnlyList<SessionInPages> Sessions);

/// <summary>A book read in the stretch. No title when Catalog could not be asked.</summary>
public sealed record ReadBook(Guid EntryId, string? Title);

/// <summary>
/// One sitting as the stats page sees it: which book, when, and how much in pages, unrounded —
/// null when it was logged in percent of a book whose length nobody knows.
/// </summary>
public sealed record SessionInPages(Guid EntryId, DateTimeOffset OccurredAt, decimal? Pages, int? DurationMinutes, bool FromDevice);

/// <summary>
/// The answer to logging or correcting a stretch of reading: what was recorded, and the entry as
/// it now stands. The second half is the useful one — progress is worked out from the sessions,
/// so recording one moves it — and it arrives with the first rather than costing a second trip.
/// </summary>
public sealed record LoggedSession(ReadingSessionView Session, LibraryEntry Entry);

/// <summary>
/// A stretch of reading, as the reader describes it when logging or correcting one:
/// <paramref name="Amount"/> is how much they read, in the method they track this book by.
/// </summary>
public sealed record NewSession(
    decimal Amount,
    DateTimeOffset? OccurredAt,
    int? DurationMinutes);

/// <summary>
/// A book put on the shelf at a chosen status (<see cref="GatewayLibraryClient.AddAsAsync"/>):
/// the entry as it now stands, or why it could not be added; and whether the book went on but its
/// status could not be set, which leaves it on Want to read.
/// </summary>
public sealed record Shelved(LibraryEntry? Entry, AddToLibraryProblem? Problem, bool StatusRefused);

/// <summary>
/// Why a reader's shelf could not be shown. The causes are told apart because they call
/// for different things from the reader: signing in again, waiting, or nothing at all.
/// </summary>
public enum LibraryUnavailable
{
    /// <summary>There is no session, or it has ended. The Gateway said 401.</summary>
    NotSignedIn,

    /// <summary>The Gateway itself could not be reached — a network failure, not a refusal.</summary>
    GatewayUnreachable,
}

/// <summary>
/// Why a Book could not be added to the reader's library. Told apart because they call for
/// different things from the reader: signing in again, waiting, or nothing at all — the book is
/// already there.
/// </summary>
public enum AddToLibraryProblem
{
    /// <summary>There is no session, or it has ended. The Gateway said 401.</summary>
    NotSignedIn,

    /// <summary>The Gateway itself could not be reached — a network failure, not a refusal.</summary>
    GatewayUnreachable,

    /// <summary>This reader already has this Book. One entry per book per reader.</summary>
    AlreadyInLibrary,
}

/// <summary>
/// Why a change to something already on the shelf did not happen. Separate from
/// <see cref="AddToLibraryProblem"/> because these operations can fail in a way adding cannot:
/// the server can validate the change and refuse it, with a reason worth repeating to the reader.
/// </summary>
public enum LibraryChangeProblem
{
    /// <summary>There is no session, or it has ended. The Gateway said 401.</summary>
    NotSignedIn,

    /// <summary>The Gateway itself could not be reached — a network failure, not a refusal.</summary>
    GatewayUnreachable,

    /// <summary>Gone, or never this reader's to change. Library does not distinguish the two.</summary>
    NoLongerThere,

    /// <summary>
    /// Library considered the change and said no — a session ending before it starts, a position
    /// past the end of the book. <c>Reasons</c> carries what it said.
    /// </summary>
    Refused,
}

/// <summary>The outcome of a change that returns something, such as the updated entry.</summary>
public sealed record LibraryChange<T>(T? Value, LibraryChangeProblem? Problem, IReadOnlyList<string> Reasons)
    where T : class
{
    public bool Ok => Problem is null;

    public static LibraryChange<T> Succeeded(T value) => new(value, null, []);

    public static LibraryChange<T> Failed(LibraryChangeProblem problem, IReadOnlyList<string>? reasons = null) =>
        new(null, problem, reasons ?? []);
}

/// <summary>The outcome of a change that returns nothing, such as a removal.</summary>
public sealed record LibraryChange(LibraryChangeProblem? Problem, IReadOnlyList<string> Reasons)
{
    public bool Ok => Problem is null;

    public static LibraryChange Succeeded() => new(null, []);

    public static LibraryChange Failed(LibraryChangeProblem problem, IReadOnlyList<string>? reasons = null) =>
        new(problem, reasons ?? []);
}

/// <summary>
/// The browser's window onto a reader's Library, reached through the Gateway rather than
/// Library directly — nothing in this application is allowed to know Library's address.
///
/// It is also where the shelf is told it changed: the changes that can move the count of finished
/// books (a status that took, an entry removed) announce themselves, so no caller has to remember
/// to — the header's goal badge cannot be left stale by a caller that forgot.
/// </summary>
public sealed class GatewayLibraryClient(HttpClient httpClient, ShelfChanges shelf)
{
    /// <summary>
    /// The reader's shelf, narrowed to one ReadingStatus when <c>status</c> is given. The filter
    /// is passed through to Library rather than applied here, so "Reading" means whatever Library
    /// says it means.
    /// </summary>
    public async Task<(IReadOnlyList<LibraryEntry>? Entries, LibraryUnavailable? Problem)> GetEntriesAsync(
        string? status,
        CancellationToken cancellationToken)
    {
        var url = string.IsNullOrWhiteSpace(status) ? "api/library" : $"api/library?status={Uri.EscapeDataString(status)}";

        HttpResponseMessage response;

        try
        {
            response = await httpClient.GetAsync(url, cancellationToken);
        }
        catch (HttpRequestException)
        {
            // Distinguishable from an empty shelf: nobody answered at all, as opposed to
            // somebody answering "you have no books" or "you are not signed in".
            return (null, LibraryUnavailable.GatewayUnreachable);
        }

        if (response.StatusCode is HttpStatusCode.Unauthorized)
        {
            return (null, LibraryUnavailable.NotSignedIn);
        }

        response.EnsureSuccessStatusCode();

        var entries = await response.Content.ReadFromJsonAsync<IReadOnlyList<LibraryEntry>>(cancellationToken);
        return (entries ?? [], null);
    }

    public async Task<(LibraryEntry? Entry, AddToLibraryProblem? Problem)> AddAsync(
        Guid bookId,
        CancellationToken cancellationToken)
    {
        HttpResponseMessage response;

        try
        {
            response = await httpClient.PostAsJsonAsync("api/library", new { bookId }, cancellationToken);
        }
        catch (HttpRequestException)
        {
            return (null, AddToLibraryProblem.GatewayUnreachable);
        }

        if (response.StatusCode is HttpStatusCode.Unauthorized)
        {
            return (null, AddToLibraryProblem.NotSignedIn);
        }

        if (response.StatusCode is HttpStatusCode.Conflict)
        {
            return (null, AddToLibraryProblem.AlreadyInLibrary);
        }

        response.EnsureSuccessStatusCode();

        var entry = await response.Content.ReadFromJsonAsync<LibraryEntry>(cancellationToken);
        return (entry, null);
    }

    /// <summary>
    /// Puts a book on the shelf as <paramref name="status"/>. Library adds only as Want to read,
    /// so any other status is a second call; the book is on the shelf either way once the first
    /// has gone through, and <see cref="Shelved.StatusRefused"/> says when the second did not —
    /// the book is then on Want to read, and the reader should be told so.
    /// </summary>
    public async Task<Shelved> AddAsAsync(
        Guid bookId,
        string status,
        CancellationToken cancellationToken,
        DateOnly? finishedOn = null)
    {
        var (entry, problem) = await AddAsync(bookId, cancellationToken);

        if (entry is null || status == entry.Status)
        {
            return new(entry, problem, StatusRefused: false);
        }

        var moved = await SetStatusAsync(entry.Id, status, cancellationToken, finishedOn);
        return new(moved.Value ?? entry, null, StatusRefused: !moved.Ok);
    }

    /// <summary>
    /// Moves a book to a status. <paramref name="finishedOn"/> goes only with Finished, and says
    /// which day it was when the reader knows — an import does — rather than today. A status that
    /// took is announced to the shelf (<see cref="ShelfChanges"/>): it may have moved the count of
    /// finished books.
    /// </summary>
    public async Task<LibraryChange<LibraryEntry>> SetStatusAsync(
        Guid entryId,
        string status,
        CancellationToken cancellationToken,
        DateOnly? finishedOn = null)
    {
        var change = await ChangeAsync<LibraryEntry>(
            client => client.PutAsJsonAsync($"api/library/{entryId}/status", new { status, finishedOn }, cancellationToken),
            cancellationToken);

        if (change.Ok)
        {
            shelf.Announce();
        }

        return change;
    }

    public Task<LibraryChange<ReadingGoal>> GetGoalAsync(int year, CancellationToken cancellationToken) =>
        ChangeAsync<ReadingGoal>(client => client.GetAsync($"api/library/goals/{year}", cancellationToken), cancellationToken);

    public Task<LibraryChange<ReadingGoal>> SetGoalAsync(int year, int books, CancellationToken cancellationToken) =>
        ChangeAsync<ReadingGoal>(
            client => client.PutAsJsonAsync($"api/library/goals/{year}", new { books }, cancellationToken),
            cancellationToken);

    public Task<LibraryChange> ClearGoalAsync(int year, CancellationToken cancellationToken) =>
        ChangeAsync(client => client.DeleteAsync($"api/library/goals/{year}", cancellationToken), cancellationToken);

    public Task<LibraryChange<LibraryEntry>> SetTrackingMethodAsync(
        Guid entryId,
        string trackingMethod,
        CancellationToken cancellationToken) =>
        ChangeAsync<LibraryEntry>(
            client => client.PutAsJsonAsync(
                $"api/library/{entryId}/tracking-method",
                new { trackingMethod },
                cancellationToken),
            cancellationToken);

    /// <summary>
    /// Sets this reader's own page count for their edition. A null <c>totalPages</c> clears the
    /// override, going back to Catalog's count.
    /// </summary>
    public Task<LibraryChange<LibraryEntry>> SetPageCountAsync(
        Guid entryId,
        int? totalPages,
        CancellationToken cancellationToken) =>
        ChangeAsync<LibraryEntry>(
            client => client.PutAsJsonAsync(
                $"api/library/{entryId}/page-count",
                new { totalPages },
                cancellationToken),
            cancellationToken);

    /// <summary>
    /// Points an entry at a different Book — the edition the reader actually has. Status,
    /// sessions and the device link stay with the entry; only the length changes.
    /// </summary>
    public Task<LibraryChange<LibraryEntry>> ChangeBookAsync(
        Guid entryId,
        Guid bookId,
        CancellationToken cancellationToken) =>
        ChangeAsync<LibraryEntry>(
            client => client.PutAsJsonAsync(
                $"api/library/{entryId}/book",
                new { bookId },
                cancellationToken),
            cancellationToken);

    public Task<LibraryChange<IReadOnlyList<ReadingSessionView>>> GetSessionsAsync(
        Guid entryId,
        CancellationToken cancellationToken) =>
        ChangeAsync<IReadOnlyList<ReadingSessionView>>(
            client => client.GetAsync($"api/library/{entryId}/sessions", cancellationToken),
            cancellationToken);

    /// <summary>Every sitting across the shelf from <paramref name="from"/> up to <paramref name="to"/>, oldest first, and the books they were in.</summary>
    public Task<LibraryChange<ReadingSpan>> GetReadingAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken) =>
        ChangeAsync<ReadingSpan>(
            client => client.GetAsync(
                $"api/library/reading?from={Uri.EscapeDataString(from.ToString("O"))}&to={Uri.EscapeDataString(to.ToString("O"))}",
                cancellationToken),
            cancellationToken);

    public Task<LibraryChange<LoggedSession>> LogSessionAsync(
        Guid entryId,
        NewSession session,
        CancellationToken cancellationToken) =>
        ChangeAsync<LoggedSession>(
            client => client.PostAsJsonAsync($"api/library/{entryId}/sessions", session, cancellationToken),
            cancellationToken);

    public Task<LibraryChange<LoggedSession>> CorrectSessionAsync(
        Guid entryId,
        Guid sessionId,
        NewSession session,
        CancellationToken cancellationToken) =>
        ChangeAsync<LoggedSession>(
            client => client.PutAsJsonAsync(
                $"api/library/{entryId}/sessions/{sessionId}",
                session,
                cancellationToken),
            cancellationToken);

    public Task<LibraryChange> DeleteSessionAsync(
        Guid entryId,
        Guid sessionId,
        CancellationToken cancellationToken) =>
        ChangeAsync(
            client => client.DeleteAsync($"api/library/{entryId}/sessions/{sessionId}", cancellationToken),
            cancellationToken);

    /// <summary>
    /// Takes a book off the shelf. Announced to the shelf when it went, whatever it was: a
    /// finished one was counted, and the client does not know which this was.
    /// </summary>
    public async Task<LibraryChange> RemoveAsync(Guid entryId, CancellationToken cancellationToken)
    {
        var change = await ChangeAsync(
            client => client.DeleteAsync($"api/library/{entryId}", cancellationToken),
            cancellationToken);

        if (change.Ok)
        {
            shelf.Announce();
        }

        return change;
    }

    /// <summary>
    /// Every change to something already on the shelf fails in the same four ways, so they are
    /// read the same way once rather than nine times.
    /// </summary>
    private async Task<LibraryChange<T>> ChangeAsync<T>(
        Func<HttpClient, Task<HttpResponseMessage>> send,
        CancellationToken cancellationToken)
        where T : class
    {
        HttpResponseMessage response;

        try
        {
            response = await send(httpClient);
        }
        catch (HttpRequestException)
        {
            return LibraryChange<T>.Failed(LibraryChangeProblem.GatewayUnreachable);
        }

        if (ProblemWith(response) is { } problem)
        {
            return LibraryChange<T>.Failed(problem, await ReasonsFrom(response, problem, cancellationToken));
        }

        response.EnsureSuccessStatusCode();

        var value = await response.Content.ReadFromJsonAsync<T>(cancellationToken);

        return value is null
            ? LibraryChange<T>.Failed(LibraryChangeProblem.NoLongerThere)
            : LibraryChange<T>.Succeeded(value);
    }

    private async Task<LibraryChange> ChangeAsync(
        Func<HttpClient, Task<HttpResponseMessage>> send,
        CancellationToken cancellationToken)
    {
        HttpResponseMessage response;

        try
        {
            response = await send(httpClient);
        }
        catch (HttpRequestException)
        {
            return LibraryChange.Failed(LibraryChangeProblem.GatewayUnreachable);
        }

        if (ProblemWith(response) is { } problem)
        {
            return LibraryChange.Failed(problem, await ReasonsFrom(response, problem, cancellationToken));
        }

        response.EnsureSuccessStatusCode();

        return LibraryChange.Succeeded();
    }

    private static LibraryChangeProblem? ProblemWith(HttpResponseMessage response) => response.StatusCode switch
    {
        HttpStatusCode.Unauthorized => LibraryChangeProblem.NotSignedIn,
        HttpStatusCode.NotFound => LibraryChangeProblem.NoLongerThere,
        HttpStatusCode.BadRequest => LibraryChangeProblem.Refused,
        _ => null,
    };

    /// <summary>
    /// What Library said when it refused. Its validation problems name the reader's mistake in
    /// words already written for a person to read — "a session cannot end before it starts" —
    /// so they are repeated rather than translated into something vaguer here.
    /// </summary>
    private static async Task<IReadOnlyList<string>> ReasonsFrom(
        HttpResponseMessage response,
        LibraryChangeProblem problem,
        CancellationToken cancellationToken)
    {
        if (problem is not LibraryChangeProblem.Refused)
        {
            return [];
        }

        try
        {
            var details = await response.Content.ReadFromJsonAsync<ValidationProblem>(cancellationToken);

            return details?.Errors?.SelectMany(field => field.Value).ToArray() ?? [];
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            // A refusal we cannot read is still a refusal. The caller says so in general terms
            // rather than showing nothing at all.
            return [];
        }
    }

    private sealed record ValidationProblem(Dictionary<string, string[]>? Errors);
}

public sealed class GatewayOptions
{
    public const string SectionName = "Gateway";

    public Uri BaseAddress { get; set; } = new("http://localhost:5100/");
}
