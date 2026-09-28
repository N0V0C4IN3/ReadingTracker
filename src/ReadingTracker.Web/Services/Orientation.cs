using Microsoft.JSInterop;

namespace ReadingTracker.Web.Services;

/// <summary>
/// Whether this is a phone held on its side: a short landscape screen whose main pointer is a
/// finger (readingTracker.sideways in index.html). A desktop window of any shape has a mouse, and
/// a tablet on its side is too tall. The shelf is a cover flow then, and the list otherwise.
///
/// Known from the start, because the browser is asked synchronously when this is made, so a page
/// can draw the right thing the first time; and kept up to date once watched, as the phone turns.
/// One scoped instance per app.
/// </summary>
public sealed class Orientation(IJSRuntime js) : IAsyncDisposable
{
    public bool Sideways { get; private set; } = ((IJSInProcessRuntime)js).Invoke<bool>("readingTracker.isSideways");

    /// <summary>Raised when the phone has been turned one way or the other.</summary>
    public event Action? Changed;

    private DotNetObjectReference<Orientation>? _self;
    private IJSObjectReference? _watch;

    /// <summary>Starts listening for the phone being turned. Safe to call more than once.</summary>
    public async Task WatchAsync()
    {
        if (_self is not null)
        {
            return;
        }

        _self = DotNetObjectReference.Create(this);
        _watch = await js.InvokeAsync<IJSObjectReference>("readingTracker.watchSideways", _self);

        // Turned while the watch was being set up, and so never heard: asked again.
        Turned(await js.InvokeAsync<bool>("readingTracker.isSideways"));
    }

    [JSInvokable]
    public void Turned(bool sideways)
    {
        if (sideways != Sideways)
        {
            Sideways = sideways;
            Changed?.Invoke();
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_watch is not null)
            {
                await _watch.InvokeVoidAsync("stop");
                await _watch.DisposeAsync();
            }
        }
        catch (JSDisconnectedException)
        {
        }

        _self?.Dispose();
    }
}
