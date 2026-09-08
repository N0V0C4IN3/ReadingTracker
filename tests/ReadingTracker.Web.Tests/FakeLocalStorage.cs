using Microsoft.JSInterop;

namespace ReadingTracker.Web.Tests;

/// <summary>
/// The browser's localStorage, in a dictionary. Only the three calls
/// <see cref="ReadingTracker.Web.Services.DevSignInSession"/> makes are answered — anything else
/// is a mistake this should report rather than quietly return null for.
/// </summary>
public sealed class FakeLocalStorage : IJSRuntime
{
    private readonly Dictionary<string, string> _items = [];

    public string? this[string key] => _items.GetValueOrDefault(key);

    public void Seed(string key, string value) => _items[key] = value;

    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
    {
        var key = (string)args![0]!;

        switch (identifier)
        {
            case "localStorage.getItem":
                return ValueTask.FromResult((TValue)(object?)_items.GetValueOrDefault(key)!);

            case "localStorage.setItem":
                _items[key] = (string)args[1]!;
                return default;

            case "localStorage.removeItem":
                _items.Remove(key);
                return default;

            default:
                throw new NotSupportedException($"Unexpected JS call: {identifier}");
        }
    }

    public ValueTask<TValue> InvokeAsync<TValue>(
        string identifier,
        CancellationToken cancellationToken,
        object?[]? args) =>
        InvokeAsync<TValue>(identifier, args);
}
