using System.Net;
using System.Net.Http.Headers;

namespace ReadingTracker.Gateway.Tests;

/// <summary>
/// The cover endpoint fetches a jacket on the browser's behalf so the book page can read its
/// colours. It is a narrow thing on purpose — the hosts covers come from, images, a reader
/// signed in in person — and these hold it to that, since the easy mistake is for it to
/// become a proxy for anything.
/// </summary>
[Collection(GatewayCollection.Name)]
public sealed class CoverTests(GatewayFixture fixture)
{
    private const string GoogleCover = "https://books.google.com/books/content?id=abc&printsec=frontcover&img=1";

    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46];

    private void CoverHostAnswers(HttpStatusCode status, byte[] body, string mediaType)
    {
        fixture.CoverHosts.Requests.Clear();
        fixture.CoverHosts.Respond = _ => new HttpResponseMessage(status)
        {
            Content = new ByteArrayContent(body) { Headers = { ContentType = new MediaTypeHeaderValue(mediaType) } },
        };
    }

    [Fact]
    public async Task Hands_the_cover_back_as_the_image_it_is_and_lets_the_browser_keep_it()
    {
        CoverHostAnswers(HttpStatusCode.OK, Jpeg, "image/jpeg");

        var response = await fixture.ClientFor("reader")
            .GetAsync($"/api/covers?url={Uri.EscapeDataString(GoogleCover)}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/jpeg", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(Jpeg, await response.Content.ReadAsByteArrayAsync());
        Assert.Equal(TimeSpan.FromDays(1), response.Headers.CacheControl?.MaxAge);
        Assert.Equal(GoogleCover, fixture.CoverHosts.Requests.Single().RequestUri!.ToString());
    }

    [Fact]
    public async Task Fetches_from_a_cover_host_only()
    {
        CoverHostAnswers(HttpStatusCode.OK, Jpeg, "image/jpeg");

        var response = await fixture.ClientFor("reader")
            .GetAsync("/api/covers?url=" + Uri.EscapeDataString("https://internal.example/secret.png"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(fixture.CoverHosts.Requests);
    }

    [Fact]
    public async Task Hands_back_nothing_that_is_not_an_image()
    {
        CoverHostAnswers(HttpStatusCode.OK, "<html>not a cover</html>"u8.ToArray(), "text/html");

        var response = await fixture.ClientFor("reader")
            .GetAsync($"/api/covers?url={Uri.EscapeDataString(GoogleCover)}");

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
    }

    [Fact]
    public async Task Says_the_host_failed_rather_than_passing_its_failure_off_as_a_cover()
    {
        CoverHostAnswers(HttpStatusCode.NotFound, [], "image/jpeg");

        var response = await fixture.ClientFor("reader")
            .GetAsync($"/api/covers?url={Uri.EscapeDataString(GoogleCover)}");

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
    }

    [Fact]
    public async Task Is_for_a_reader_signed_in_in_person()
    {
        CoverHostAnswers(HttpStatusCode.OK, Jpeg, "image/jpeg");
        var deviceToken = await fixture.MintDeviceTokenAsync("kindle-owner", "Kindle");

        var anonymous = await fixture.CreateClient().GetAsync($"/api/covers?url={Uri.EscapeDataString(GoogleCover)}");
        var device = await fixture.ClientWithToken(deviceToken).GetAsync($"/api/covers?url={Uri.EscapeDataString(GoogleCover)}");

        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, device.StatusCode);
        Assert.Empty(fixture.CoverHosts.Requests);
    }
}
