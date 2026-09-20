using System.Net;
using System.Text;
using ReadingTracker.Web.Services;

namespace ReadingTracker.Web.Tests;

public sealed class GatewayDevicesClientTests
{
    [Fact]
    public async Task Lists_the_readers_devices()
    {
        var (client, gateway) = CreateClient();
        var id = Guid.NewGuid();
        gateway.Respond = _ => StubHttpMessageHandler.Json($$"""
            [
              {
                "id": "{{id}}",
                "name": "Kindle",
                "createdAt": "2026-09-20T10:00:00+00:00",
                "lastUsedAt": "2026-09-20T11:30:00+00:00"
              }
            ]
            """);

        var listed = await client.ListAsync(CancellationToken.None);

        Assert.True(listed.Ok);
        var device = Assert.Single(listed.Value!);
        Assert.Equal(id, device.Id);
        Assert.Equal("Kindle", device.Name);
        Assert.Equal(new DateTimeOffset(2026, 9, 20, 11, 30, 0, TimeSpan.Zero), device.LastUsedAt);
        Assert.Equal("http://gateway.test/api/devices", gateway.Requests.Single().RequestUri!.ToString());
    }

    [Fact]
    public async Task Mints_a_device_and_hands_back_the_token()
    {
        var (client, gateway) = CreateClient();
        var id = Guid.NewGuid();
        string? body = null;
        gateway.RespondAsync = async (request, cancellationToken) =>
        {
            body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return Json($$"""
                {
                  "id": "{{id}}",
                  "name": "Kindle",
                  "createdAt": "2026-09-20T10:00:00+00:00",
                  "token": "rt_abcdef"
                }
                """, HttpStatusCode.Created);
        };

        var minted = await client.MintAsync("Kindle", CancellationToken.None);

        Assert.True(minted.Ok);
        Assert.Equal(id, minted.Value!.Id);
        Assert.Equal("rt_abcdef", minted.Value.Token);
        Assert.Equal(HttpMethod.Post, gateway.Requests.Single().Method);
        Assert.Contains("\"name\":\"Kindle\"", body);
    }

    [Fact]
    public async Task Says_when_the_reader_has_minted_enough_for_the_moment()
    {
        var (client, gateway) = CreateClient();
        gateway.Respond = _ => new HttpResponseMessage(HttpStatusCode.TooManyRequests);

        var minted = await client.MintAsync("Kindle", CancellationToken.None);

        Assert.Equal(DeviceProblem.TooManyRequests, minted.Problem);
    }

    [Fact]
    public async Task Repeats_what_the_gateway_said_when_it_refused_the_name()
    {
        var (client, gateway) = CreateClient();
        gateway.Respond = _ => Json("""
            { "errors": { "name": ["Give the device a name."] } }
            """, HttpStatusCode.BadRequest);

        var minted = await client.MintAsync("", CancellationToken.None);

        Assert.Equal(DeviceProblem.Refused, minted.Problem);
        Assert.Equal(["Give the device a name."], minted.Reasons);
    }

    [Fact]
    public async Task Says_when_a_device_can_only_be_minted_in_person()
    {
        var (client, gateway) = CreateClient();
        gateway.Respond = _ => new HttpResponseMessage(HttpStatusCode.Forbidden);

        var minted = await client.MintAsync("Kindle", CancellationToken.None);

        Assert.Equal(DeviceProblem.NotInPerson, minted.Problem);
    }

    [Fact]
    public async Task Revokes_a_device()
    {
        var (client, gateway) = CreateClient();
        var id = Guid.NewGuid();
        gateway.Respond = _ => new HttpResponseMessage(HttpStatusCode.NoContent);

        var revoked = await client.RevokeAsync(id, CancellationToken.None);

        Assert.True(revoked.Ok);
        var request = gateway.Requests.Single();
        Assert.Equal(HttpMethod.Delete, request.Method);
        Assert.Equal($"http://gateway.test/api/devices/{id}", request.RequestUri!.ToString());
    }

    [Fact]
    public async Task Reports_a_device_that_is_already_gone()
    {
        var (client, gateway) = CreateClient();
        gateway.Respond = _ => new HttpResponseMessage(HttpStatusCode.NotFound);

        var revoked = await client.RevokeAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(DeviceProblem.NoLongerThere, revoked.Problem);
    }

    [Fact]
    public async Task Reports_when_the_session_has_ended()
    {
        var (client, gateway) = CreateClient();
        gateway.Respond = _ => new HttpResponseMessage(HttpStatusCode.Unauthorized);

        Assert.Equal(DeviceProblem.NotSignedIn, (await client.ListAsync(CancellationToken.None)).Problem);
        Assert.Equal(DeviceProblem.NotSignedIn, (await client.MintAsync("Kindle", CancellationToken.None)).Problem);
        Assert.Equal(DeviceProblem.NotSignedIn, (await client.RevokeAsync(Guid.NewGuid(), CancellationToken.None)).Problem);
    }

    [Fact]
    public async Task Reports_when_the_gateway_cannot_be_reached_at_all()
    {
        var (client, gateway) = CreateClient();
        gateway.Respond = _ => throw new HttpRequestException("connection refused");

        Assert.Equal(DeviceProblem.GatewayUnreachable, (await client.ListAsync(CancellationToken.None)).Problem);
        Assert.Equal(DeviceProblem.GatewayUnreachable, (await client.MintAsync("Kindle", CancellationToken.None)).Problem);
        Assert.Equal(DeviceProblem.GatewayUnreachable, (await client.RevokeAsync(Guid.NewGuid(), CancellationToken.None)).Problem);
    }

    private static HttpResponseMessage Json(string body, HttpStatusCode status) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static (GatewayDevicesClient Client, StubHttpMessageHandler Gateway) CreateClient()
    {
        var gateway = new StubHttpMessageHandler();
        var httpClient = new HttpClient(gateway) { BaseAddress = new Uri("http://gateway.test/") };
        return (new GatewayDevicesClient(httpClient), gateway);
    }
}
