using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace ReadingTracker.Gateway.Tests;

[Collection(GatewayCollection.Name)]
public sealed class DevSignInIsOffByDefaultTests(GatewayFixture fixture)
{
    [Fact]
    public async Task The_dev_sign_in_endpoint_is_not_reachable_anonymously_when_not_enabled()
    {
        var client = fixture.CreateClient();

        var response = await client.PostAsync("/dev/sign-in", JsonContent.Create(new { }));

        // Not mapped at all when disabled, so the Gateway's fallback policy — which requires
        // authentication even for requests that matched no endpoint — is what actually answers.
        // Either way, an anonymous caller gets no token out of it.
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_token_shaped_like_a_dev_sign_in_token_is_rejected_when_not_enabled()
    {
        var token = new JsonWebTokenHandler().CreateToken(
            new SecurityTokenDescriptor
            {
                Issuer = DevSignIn.Issuer,
                Audience = FakeGoogle.ClientId,
                Expires = DateTime.UtcNow.AddHours(1),
                Claims = new Dictionary<string, object> { ["sub"] = "someone" },
                SigningCredentials = new SigningCredentials(
                    new SymmetricSecurityKey(RandomNumberGenerator.GetBytes(32)),
                    SecurityAlgorithms.HmacSha256),
            });

        var response = await fixture.ClientWithToken(token).GetAsync("/api/library");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}

[Collection(DevSignInCollection.Name)]
public sealed class DevSignInWhenEnabledTests(DevSignInFixture fixture)
{
    [Fact]
    public async Task Mints_a_token_the_gateway_itself_accepts_as_the_requested_reader()
    {
        fixture.Downstream.Requests.Clear();
        fixture.Downstream.Respond = _ => new HttpResponseMessage(HttpStatusCode.OK);

        var anonymous = fixture.CreateClient();
        var signIn = await anonymous.PostAsJsonAsync("/dev/sign-in", new { readerId = "dev-tester-1" });
        Assert.Equal(HttpStatusCode.OK, signIn.StatusCode);

        var body = await signIn.Content.ReadFromJsonAsync<DevSignInResponse>();

        var authenticated = fixture.CreateClient();
        authenticated.DefaultRequestHeaders.Authorization = new("Bearer", body!.IdToken);

        var response = await authenticated.GetAsync("/api/library");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("dev-tester-1", Assert.Single(fixture.Downstream.Requests).Headers.GetValues("X-Reader-Id").Single());
    }

    private sealed record DevSignInResponse(string IdToken);
}
