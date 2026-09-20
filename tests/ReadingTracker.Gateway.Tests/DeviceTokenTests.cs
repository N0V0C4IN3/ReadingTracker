using System.Net;
using System.Net.Http.Json;

namespace ReadingTracker.Gateway.Tests;

/// <summary>
/// A DeviceToken is a Reader's stand-in for a device that cannot sign in with Google. Like
/// <see cref="ReaderIdentityTests"/>, what matters is what reached the service behind the
/// Gateway: a token that is accepted must arrive there as exactly the reader who minted it.
/// </summary>
[Collection(GatewayCollection.Name)]
public sealed class DeviceTokenTests(GatewayFixture fixture)
{
    private const string ReaderHeader = "X-Reader-Id";

    [Fact]
    public async Task Acts_as_the_reader_who_minted_it()
    {
        var seen = fixture.ResetWithOkResponses();
        var token = await fixture.MintDeviceTokenAsync("kindle-owner", "Kindle");

        await fixture.ClientWithToken(token).GetAsync("/api/library");

        Assert.Equal("kindle-owner", seen.Single().Headers.GetValues(ReaderHeader).Single());
    }

    [Fact]
    public async Task Hands_the_secret_over_once_at_minting()
    {
        var response = await fixture.ClientFor("minting-reader")
            .PostAsJsonAsync("/api/devices", new { name = "Kindle" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var minted = await response.Content.ReadFromJsonAsync<Minted>();
        Assert.NotEqual(Guid.Empty, minted!.Id);
        Assert.Equal("Kindle", minted.Name);
        Assert.StartsWith("rt_", minted.Token);
        // Long enough that guessing one is hopeless: at least 32 random bytes, base64url'd.
        Assert.True(minted.Token.Length >= "rt_".Length + 43, minted.Token);
    }

    [Fact]
    public async Task Turns_away_a_token_shaped_like_a_device_token_that_was_never_minted()
    {
        var seen = fixture.ResetWithOkResponses();

        var response = await fixture.ClientWithToken("rt_" + new string('a', 43)).GetAsync("/api/library");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains(response.Headers.WwwAuthenticate, header => header.Scheme == "Bearer");
        Assert.Empty(seen);
    }

    [Fact]
    public async Task Turns_away_a_device_token_that_is_only_the_prefix()
    {
        var seen = fixture.ResetWithOkResponses();

        var response = await fixture.ClientWithToken("rt_").GetAsync("/api/library");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(seen);
    }

    [Fact]
    public async Task Does_not_let_a_device_mint_device_tokens()
    {
        var token = await fixture.MintDeviceTokenAsync("careful-reader", "Kindle");

        var response = await fixture.ClientWithToken(token)
            .PostAsJsonAsync("/api/devices", new { name = "A successor" });

        // Forbidden, not Unauthorized: the Gateway knows exactly who this is. A token lifted
        // from a lost device must not be able to mint itself successors faster than the Reader
        // revokes them.
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Does_not_leak_the_device_token_onward_to_the_services_behind_it()
    {
        var seen = fixture.ResetWithOkResponses();
        var token = await fixture.MintDeviceTokenAsync("private-device-owner", "Kindle");

        await fixture.ClientWithToken(token).GetAsync("/api/library");

        Assert.DoesNotContain(seen.Single().Headers, header =>
            header.Key.Equals("Authorization", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Throws_away_a_reader_header_the_device_made_up()
    {
        var seen = fixture.ResetWithOkResponses();
        var token = await fixture.MintDeviceTokenAsync("the-real-device-owner", "Kindle");
        var client = fixture.ClientWithToken(token);
        client.DefaultRequestHeaders.Add(ReaderHeader, "somebody-elses-shelf");

        await client.GetAsync("/api/library");

        Assert.Equal("the-real-device-owner", seen.Single().Headers.GetValues(ReaderHeader).Single());
    }

    [Fact]
    public async Task Insists_the_device_be_named()
    {
        var response = await fixture.ClientFor("hasty-reader")
            .PostAsJsonAsync("/api/devices", new { name = "  " });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private sealed record Minted(Guid Id, string Name, DateTimeOffset CreatedAt, string Token);
}
