using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.JSInterop;

namespace ReadingTracker.Web.Services;

/// <summary>
/// Keeps the reader's place on the shelf across a visit to another page. When the shelf is left
/// — for a book's page, most often — it asks the shelf what its search is (<paramref name="search"/>)
/// and measures where the screen is (js/shelfPlace.js), and puts both in ShelfMemory while the
/// shelf is still on screen to be measured. The shelf that comes back takes the place (Take) and,
/// once it has rendered, has the screen put back (PutBackAsync).
///
/// A place is the reader's who left it: one taken by anybody else is let go, so a reader signing
/// in where another signed out (dev sign-in does, in place) never gets the other's search.
/// </summary>
public sealed class ShelfPlaceKeeper : IAsyncDisposable
{
    private readonly NavigationManager _navigation;
    private readonly ShelfMemory _memory;
    private readonly IJSRuntime _js;
    private readonly Func<ShelfSearch?> _search;
    private readonly Func<ElementReference> _dock;
    private readonly IDisposable _leaving;
    private IJSObjectReference? _module;

    /// <param name="search">The shelf's search as it stands, and whose shelf it is; null when nobody's shelf is showing.</param>
    /// <param name="dock">The search dock, whose results scroll inside it on a phone.</param>
    public ShelfPlaceKeeper(
        NavigationManager navigation,
        ShelfMemory memory,
        IJSRuntime js,
        Func<ShelfSearch?> search,
        Func<ElementReference> dock)
    {
        _navigation = navigation;
        _memory = memory;
        _js = js;
        _search = search;
        _dock = dock;
        _leaving = navigation.RegisterLocationChangingHandler(LeavingAsync);
    }

    /// <summary>The place <paramref name="reader"/> left the shelf at, taken once; null if there is none of theirs.</summary>
    public ShelfPlace? Take(string reader) => _memory.Return() is { } place && place.Search.Reader == reader ? place : null;

    /// <summary>Puts the screen back where it was, once the shelf it was measured on is rendered again.</summary>
    public async Task PutBackAsync(ShelfScroll scroll) =>
        await (await ModuleAsync()).InvokeVoidAsync("returnTo", _dock(), scroll);

    private async ValueTask LeavingAsync(LocationChangingContext leaving)
    {
        // Going to the shelf is not leaving it.
        if (IsShelf(leaving.TargetLocation) || _search() is not { } search)
        {
            return;
        }

        var scroll = await (await ModuleAsync()).InvokeAsync<ShelfScroll>("where", _dock());
        _memory.Leave(new ShelfPlace(search, scroll));
    }

    private bool IsShelf(string location) =>
        _navigation.ToBaseRelativePath(_navigation.ToAbsoluteUri(location).ToString()).Length == 0;

    private async Task<IJSObjectReference> ModuleAsync() =>
        _module ??= await _js.InvokeAsync<IJSObjectReference>("import", "./js/shelfPlace.js");

    public async ValueTask DisposeAsync()
    {
        _leaving.Dispose();

        if (_module is null)
        {
            return;
        }

        try
        {
            await _module.DisposeAsync();
        }
        catch (JSDisconnectedException)
        {
            // The page went with the circuit; there is nothing to let go of.
        }
    }
}
