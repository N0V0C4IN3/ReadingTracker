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

    [Fact]
    public async Task Lists_a_readers_devices_without_their_secrets()
    {
        var reader = fixture.ClientFor("lists-devices");
        await reader.PostAsJsonAsync("/api/devices", new { name = "Kindle" });
        await reader.PostAsJsonAsync("/api/devices", new { name = "Kobo" });

        var response = await reader.GetAsync("/api/devices");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var listed = (await response.Content.ReadFromJsonAsync<List<Listed>>())!;
        Assert.Equal(["Kindle", "Kobo"], listed.Select(device => device.Name));
        Assert.All(listed, device => Assert.NotEqual(Guid.Empty, device.Id));
        Assert.All(listed, device => Assert.Null(device.LastUsedAt));
        // The secret was handed over at minting and is not kept, so it cannot be here.
        Assert.DoesNotContain("rt_", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Never_lists_another_readers_devices()
    {
        await fixture.MintDeviceTokenAsync("has-a-kindle", "Kindle");

        var listed = await fixture.ClientFor("has-nothing").GetFromJsonAsync<List<Listed>>("/api/devices");

        Assert.Empty(listed!);
    }

    [Fact]
    public async Task Records_when_a_device_was_last_used_but_not_on_every_request()
    {
        var reader = fixture.ClientFor("watches-last-used");
        var minted = await (await reader.PostAsJsonAsync("/api/devices", new { name = "Kindle" }))
            .Content.ReadFromJsonAsync<Minted>();
        var device = fixture.ClientWithToken(minted!.Token);
        fixture.ResetWithOkResponses();

        await device.GetAsync("/api/library");
        var afterFirstUse = await LastUsedAsync(reader, minted.Id);
        await device.GetAsync("/api/library");
        var afterSecondUse = await LastUsedAsync(reader, minted.Id);

        Assert.NotNull(afterFirstUse);
        Assert.InRange(afterFirstUse.Value, DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow);
        // A busy Kindle must not turn every page into a write; within a minute it is the same.
        Assert.Equal(afterFirstUse, afterSecondUse);
    }

    [Fact]
    public async Task A_revoked_device_is_refused_from_then_on()
    {
        var seen = fixture.ResetWithOkResponses();
        var reader = fixture.ClientFor("revokes-a-lost-kindle");
        var minted = await (await reader.PostAsJsonAsync("/api/devices", new { name = "Kindle" }))
            .Content.ReadFromJsonAsync<Minted>();
        var device = fixture.ClientWithToken(minted!.Token);
        Assert.Equal(HttpStatusCode.OK, (await device.GetAsync("/api/library")).StatusCode);

        var revoked = await reader.DeleteAsync($"/api/devices/{minted.Id}");

        Assert.Equal(HttpStatusCode.NoContent, revoked.StatusCode);
        Assert.DoesNotContain(
            (await reader.GetFromJsonAsync<List<Listed>>("/api/devices"))!,
            device => device.Id == minted.Id);
        seen = fixture.ResetWithOkResponses();
        Assert.Equal(HttpStatusCode.Unauthorized, (await device.GetAsync("/api/library")).StatusCode);
        Assert.Empty(seen);
    }

    [Fact]
    public async Task Cannot_revoke_another_readers_device()
    {
        var owner = fixture.ClientFor("owns-a-kindle");
        var minted = await (await owner.PostAsJsonAsync("/api/devices", new { name = "Kindle" }))
            .Content.ReadFromJsonAsync<Minted>();

        var response = await fixture.ClientFor("someone-else").DeleteAsync($"/api/devices/{minted!.Id}");

        // Not found rather than forbidden: whether the id exists at all is not theirs to learn.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        fixture.ResetWithOkResponses();
        Assert.Equal(HttpStatusCode.OK, (await fixture.ClientWithToken(minted.Token).GetAsync("/api/library")).StatusCode);
    }

    [Fact]
    public async Task Does_not_let_a_device_list_or_revoke_device_tokens()
    {
        var reader = fixture.ClientFor("guards-devices");
        var minted = await (await reader.PostAsJsonAsync("/api/devices", new { name = "Kindle" }))
            .Content.ReadFromJsonAsync<Minted>();
        var device = fixture.ClientWithToken(minted!.Token);

        Assert.Equal(HttpStatusCode.Forbidden, (await device.GetAsync("/api/devices")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await device.DeleteAsync($"/api/devices/{minted.Id}")).StatusCode);
        // Still there: a device cannot revoke its siblings either.
        Assert.Contains((await reader.GetFromJsonAsync<List<Listed>>("/api/devices"))!, d => d.Id == minted.Id);
    }

    private static async Task<DateTimeOffset?> LastUsedAsync(HttpClient reader, Guid id) =>
        (await reader.GetFromJsonAsync<List<Listed>>("/api/devices"))!.Single(device => device.Id == id).LastUsedAt;

    private sealed record Minted(Guid Id, string Name, DateTimeOffset CreatedAt, string Token);

    private sealed record Listed(Guid Id, string Name, DateTimeOffset CreatedAt, DateTimeOffset? LastUsedAt);
}
