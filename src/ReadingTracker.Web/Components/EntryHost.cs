using Microsoft.AspNetCore.Components;
using ReadingTracker.Web.Services;

namespace ReadingTracker.Web.Components;

/// <summary>
/// Everything a reader can do to one LibraryEntry, for whatever is showing it: the shelf card
/// and the Book page both inherit this and render the same panels — the status select, the
/// session form, the history, the settings, the prompts — wired to the same changes.
///
/// A change is a call to Library, then the entry it answered with handed up to whoever owns the
/// list (<see cref="OnChanged"/>), which is what redraws the host. The answer is used directly:
/// Library describes an entry it has just changed exactly as it describes one on the shelf, so
/// nothing has to be re-read to find out what the change did. While a change is in flight the
/// host is <see cref="Working"/> and its controls are disabled; a refusal lands in
/// <see cref="Reasons"/> in Library's own words; a session that has ended is the whole page's
/// problem and goes up as <see cref="OnSignedOut"/>.
/// </summary>
public abstract class EntryHost : ComponentBase
{
    [Inject]
    protected GatewayLibraryClient Library { get; set; } = default!;

    [Inject]
    protected ShelfChanges Shelf { get; set; } = default!;

    [Parameter, EditorRequired]
    public required LibraryEntry Entry { get; set; }

    /// <summary>Raised when this entry changed in a way the shelf around it needs to know about.</summary>
    [Parameter]
    public EventCallback<LibraryEntry> OnChanged { get; set; }

    [Parameter]
    public EventCallback OnRemoved { get; set; }

    /// <summary>Raised when the reader's session has ended, which is the whole page's problem.</summary>
    [Parameter]
    public EventCallback OnSignedOut { get; set; }

    protected bool Working { get; private set; }

    protected IReadOnlyList<string> Reasons { get; private set; } = [];

    /// <summary>The entry's sessions, once asked for; null until then and after a change that stales them.</summary>
    protected IReadOnlyList<ReadingSessionView>? Sessions { get; private set; }

    /// <summary>
    /// Whether reading can be logged against this book: only while it is being read or waiting
    /// to be. A finished, paused or abandoned book is not being read, so the host offers the
    /// status, which is how it comes back to life, and not a form for pages it is not turning.
    /// The sessions it already has stay in History.
    /// </summary>
    protected bool Logging => Entry.Status is "Reading" or "WantToRead";

    protected bool Finished => Entry.Status == "Finished";

    protected bool Parked => Entry.Status is "OnHold" or "Dropped";

    /// <summary>
    /// Whether the history is on screen. Nothing showing it means nothing needs it fetched:
    /// changing the tracking method with the panel closed should not cost a request for a list
    /// nobody will read.
    /// </summary>
    protected abstract bool HistoryShowing { get; }

    /// <summary>The status changed, to this. The card closes a log form left open on a book set aside.</summary>
    protected virtual void StatusChanged(LibraryEntry entry)
    {
    }

    /// <summary>A session was logged. The card closes the form it came from.</summary>
    protected virtual void Logged()
    {
    }

    /// <summary>
    /// A log took the reader to the end of a book not yet finished. Offered, never done for them:
    /// people log the last page and keep going into the end matter, and a book that marks itself
    /// finished is a book arguing with its reader.
    /// </summary>
    protected virtual void ReachedEnd()
    {
    }

    protected void ClearReasons() => Reasons = [];

    protected Task LoadSessionsAsync()
    {
        Sessions = null;
        return RunAsync(token => Library.GetSessionsAsync(Entry.Id, token), sessions => Sessions = sessions);
    }

    protected async Task ChangeStatusAsync(string status)
    {
        if (status == Entry.Status)
        {
            return;
        }

        await RunAsync(token => Library.SetStatusAsync(Entry.Id, status, token), async entry =>
        {
            StatusChanged(entry);
            await Show(entry);
            // The header's goal badge counts finished books, and this may have been one.
            Shelf.Announce();
        });
    }

    protected async Task ChangeTrackingMethodAsync(string method)
    {
        if (method == Entry.TrackingMethod)
        {
            return;
        }

        await RunAsync(token => Library.SetTrackingMethodAsync(Entry.Id, method, token), async entry =>
        {
            // The history is expressed in the reader's current method, so it is now describing
            // the same sessions in different units.
            await ReloadSessionsAsync();
            await Show(entry);
        });
    }

    protected Task SetPageCountAsync(int? pages) =>
        RunAsync(token => Library.SetPageCountAsync(Entry.Id, pages, token), async entry =>
        {
            await ReloadSessionsAsync();
            await Show(entry);
        });

    /// <summary>Points the entry at another Book. True when it took; false when Library refused.</summary>
    protected async Task<bool> UseEditionAsync(Guid bookId)
    {
        var took = false;

        await RunAsync(token => Library.ChangeBookAsync(Entry.Id, bookId, token), async entry =>
        {
            took = true;
            await ReloadSessionsAsync();
            await Show(entry);
        });

        return took;
    }

    protected Task LogAsync(NewSession session) =>
        RunAsync(token => Library.LogSessionAsync(Entry.Id, session, token), async logged =>
        {
            Logged();
            await ReloadSessionsAsync();
            await Show(logged.Entry);

            // Read from the entry Library just handed back rather than from Entry, which is
            // still the one this component was rendered with — the parent has not re-rendered us
            // yet. Only a log that took the reader to the end raises this, and only while the
            // book is not already finished, so nobody is asked twice about the same book and a
            // reader who said "not yet" is left alone until they read more.
            if (logged.Entry is { Progress.PercentComplete: 100, Status: not "Finished" })
            {
                ReachedEnd();
            }
        });

    /// <summary>Corrects a session. True when it took; false when Library refused, so the form stays open.</summary>
    protected async Task<bool> CorrectAsync(Guid sessionId, NewSession session)
    {
        var took = false;

        await RunAsync(token => Library.CorrectSessionAsync(Entry.Id, sessionId, session, token), async corrected =>
        {
            took = true;
            await ReloadSessionsAsync();
            await Show(corrected.Entry);
        });

        return took;
    }

    protected Task DeleteSessionAsync(Guid sessionId) =>
        RunAsync(token => Library.DeleteSessionAsync(Entry.Id, sessionId, token), async () =>
        {
            await ReloadSessionsAsync();
            await RefreshAsync();
        });

    protected Task RemoveAsync() =>
        RunAsync(token => Library.RemoveAsync(Entry.Id, token), () => OnRemoved.InvokeAsync());

    /// <summary>Hands the entry Library answered a change with up to whoever owns it, which is what redraws this host.</summary>
    protected Task Show(LibraryEntry entry) => OnChanged.InvokeAsync(entry);

    /// <summary>
    /// Re-reads this entry from the shelf. Only deleting a session needs this: it is the one
    /// change Library answers with nothing, there being no session left to describe.
    /// </summary>
    protected async Task<LibraryEntry?> RefreshAsync()
    {
        var (entries, problem) = await Library.GetEntriesAsync(null, CancellationToken.None);

        if (problem is LibraryUnavailable.NotSignedIn)
        {
            await OnSignedOut.InvokeAsync();
            return null;
        }

        if (entries?.FirstOrDefault(entry => entry.Id == Entry.Id) is not { } fresh)
        {
            return null;
        }

        await OnChanged.InvokeAsync(fresh);

        // Handed back as well as raised, because a caller that has just changed something often
        // needs to know what it changed to before the parent has re-rendered this component.
        return fresh;
    }

    private async Task ReloadSessionsAsync()
    {
        if (!HistoryShowing)
        {
            Sessions = null;
            return;
        }

        var sessions = await Library.GetSessionsAsync(Entry.Id, CancellationToken.None);

        if (sessions.Ok)
        {
            Sessions = sessions.Value;
        }
    }

    private Task RunAsync<T>(Func<CancellationToken, Task<LibraryChange<T>>> change, Action<T> onSuccess)
        where T : class =>
        RunAsync(change, value => { onSuccess(value); return Task.CompletedTask; });

    private async Task RunAsync<T>(Func<CancellationToken, Task<LibraryChange<T>>> change, Func<T, Task> onSuccess)
        where T : class
    {
        Begin();

        try
        {
            var outcome = await change(CancellationToken.None);

            if (outcome is { Ok: true, Value: { } value })
            {
                await onSuccess(value);
                return;
            }

            await ExplainAsync(outcome.Problem, outcome.Reasons);
        }
        finally
        {
            End();
        }
    }

    private async Task RunAsync(Func<CancellationToken, Task<LibraryChange>> change, Func<Task> onSuccess)
    {
        Begin();

        try
        {
            var outcome = await change(CancellationToken.None);

            if (outcome.Ok)
            {
                await onSuccess();
                return;
            }

            await ExplainAsync(outcome.Problem, outcome.Reasons);
        }
        finally
        {
            End();
        }
    }

    // A change is usually asked for from inside one of the panels, whose own event is what
    // Blazor re-renders when it completes — not this host. So the host redraws itself at both
    // ends of a change: its controls go quiet the moment one starts, and come back when it ends.
    private void Begin()
    {
        Working = true;
        Reasons = [];
        StateHasChanged();
    }

    private void End()
    {
        Working = false;
        StateHasChanged();
    }

    private async Task ExplainAsync(LibraryChangeProblem? problem, IReadOnlyList<string> reasons)
    {
        if (problem is LibraryChangeProblem.NotSignedIn)
        {
            await OnSignedOut.InvokeAsync();
            return;
        }

        Reasons = problem switch
        {
            // Library's own wording, which already explains the mistake to a person.
            LibraryChangeProblem.Refused when reasons.Count > 0 => reasons,
            LibraryChangeProblem.Refused => ["That change was refused."],
            LibraryChangeProblem.NoLongerThere => ["That is no longer on your shelf. Reload to see where things stand."],
            _ => ["ReadingTracker could not be reached just now. Try again shortly."],
        };
    }
}
