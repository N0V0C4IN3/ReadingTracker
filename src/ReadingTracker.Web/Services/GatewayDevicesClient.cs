using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace ReadingTracker.Web.Services;

/// <summary>A DeviceToken as the Devices page sees it: everything but the secret, which the Gateway does not keep.</summary>
public sealed record Device(Guid Id, string Name, DateTimeOffset CreatedAt, DateTimeOffset? LastUsedAt);

/// <summary>
/// A DeviceToken as it is handed over at minting — the only time <paramref name="Token"/> is ever
/// seen. The Gateway keeps a hash, so once this response is gone the secret is gone with it.
/// </summary>
public sealed record MintedDevice(Guid Id, string Name, DateTimeOffset CreatedAt, string Token);

/// <summary>
/// Why something on the Devices page did not happen. Told apart because they call for different
/// things from the reader: signing in again, signing in *as a person*, waiting, or nothing at all.
/// </summary>
public enum DeviceProblem
{
    /// <summary>There is no session, or it has ended. The Gateway said 401.</summary>
    NotSignedIn,

    /// <summary>
    /// The Gateway said 403: the caller is a device, and a device cannot mint, list or revoke
    /// DeviceTokens. This application never signs in that way, so seeing it means the token it
    /// holds is not the one it thinks it is.
    /// </summary>
    NotInPerson,

    /// <summary>The Gateway itself could not be reached — a network failure, not a refusal.</summary>
    GatewayUnreachable,

    /// <summary>The Gateway said 429: this reader has minted enough for the moment.</summary>
    TooManyRequests,

    /// <summary>Already revoked, or never this reader's. The Gateway does not distinguish the two.</summary>
    NoLongerThere,

    /// <summary>The Gateway considered the request and said no. <c>Reasons</c> carries what it said.</summary>
    Refused,
}

/// <summary>The outcome of something on the Devices page that returns a value.</summary>
public sealed record DeviceChange<T>(T? Value, DeviceProblem? Problem, IReadOnlyList<string> Reasons)
    where T : class
{
    public bool Ok => Problem is null;

    public static DeviceChange<T> Succeeded(T value) => new(value, null, []);

    public static DeviceChange<T> Failed(DeviceProblem problem, IReadOnlyList<string>? reasons = null) =>
        new(null, problem, reasons ?? []);
}

/// <summary>The outcome of something on the Devices page that returns nothing, such as a revocation.</summary>
public sealed record DeviceChange(DeviceProblem? Problem)
{
    public bool Ok => Problem is null;

    public static DeviceChange Succeeded() => new((DeviceProblem?)null);

    public static DeviceChange Failed(DeviceProblem problem) => new(problem);
}

/// <summary>
/// The browser's window onto a reader's DeviceTokens. Unlike the Library and Catalog clients this
/// talks to the Gateway about the Gateway's own table (ADR-0014), but it is reached the same way,
/// at the same address, with the same token.
/// </summary>
public sealed class GatewayDevicesClient(HttpClient httpClient)
{
    public Task<DeviceChange<IReadOnlyList<Device>>> ListAsync(CancellationToken cancellationToken) =>
        ChangeAsync<IReadOnlyList<Device>>(
            client => client.GetAsync("api/devices", cancellationToken),
            cancellationToken);

    public Task<DeviceChange<MintedDevice>> MintAsync(string name, CancellationToken cancellationToken) =>
        ChangeAsync<MintedDevice>(
            client => client.PostAsJsonAsync("api/devices", new { name }, cancellationToken),
            cancellationToken);

    public async Task<DeviceChange> RevokeAsync(Guid deviceId, CancellationToken cancellationToken)
    {
        HttpResponseMessage response;

        try
        {
            response = await httpClient.DeleteAsync($"api/devices/{deviceId}", cancellationToken);
        }
        catch (HttpRequestException)
        {
            return DeviceChange.Failed(DeviceProblem.GatewayUnreachable);
        }

        if (ProblemWith(response) is { } problem)
        {
            return DeviceChange.Failed(problem);
        }

        response.EnsureSuccessStatusCode();

        return DeviceChange.Succeeded();
    }

    private async Task<DeviceChange<T>> ChangeAsync<T>(
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
            return DeviceChange<T>.Failed(DeviceProblem.GatewayUnreachable);
        }

        if (ProblemWith(response) is { } problem)
        {
            return DeviceChange<T>.Failed(problem, await ReasonsFrom(response, problem, cancellationToken));
        }

        response.EnsureSuccessStatusCode();

        var value = await response.Content.ReadFromJsonAsync<T>(cancellationToken);

        return value is null
            ? DeviceChange<T>.Failed(DeviceProblem.NoLongerThere)
            : DeviceChange<T>.Succeeded(value);
    }

    private static DeviceProblem? ProblemWith(HttpResponseMessage response) => response.StatusCode switch
    {
        HttpStatusCode.Unauthorized => DeviceProblem.NotSignedIn,
        HttpStatusCode.Forbidden => DeviceProblem.NotInPerson,
        HttpStatusCode.NotFound => DeviceProblem.NoLongerThere,
        HttpStatusCode.TooManyRequests => DeviceProblem.TooManyRequests,
        HttpStatusCode.BadRequest => DeviceProblem.Refused,
        _ => null,
    };

    /// <summary>
    /// What the Gateway said when it refused — "give the device a name" — repeated rather than
    /// translated, as the Library client does with Library's refusals.
    /// </summary>
    private static async Task<IReadOnlyList<string>> ReasonsFrom(
        HttpResponseMessage response,
        DeviceProblem problem,
        CancellationToken cancellationToken)
    {
        if (problem is not DeviceProblem.Refused)
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
            return [];
        }
    }

    private sealed record ValidationProblem(Dictionary<string, string[]>? Errors);
}
