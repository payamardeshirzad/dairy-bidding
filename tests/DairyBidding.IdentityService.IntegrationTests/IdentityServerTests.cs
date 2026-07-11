using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;

namespace DairyBidding.IdentityService.IntegrationTests;

public class IdentityServerTests(IdentityApiFactory factory) : IClassFixture<IdentityApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task OidcDiscovery_ReturnsValidMetadata()
    {
        var res = await _client.GetAsync("/.well-known/openid-configuration");

        res.StatusCode.Should().Be(HttpStatusCode.OK);

        var doc = await res.Content.ReadFromJsonAsync<JsonDocument>();
        doc!.RootElement.GetProperty("issuer").GetString().Should().NotBeNullOrEmpty();
        doc.RootElement.GetProperty("token_endpoint").GetString().Should().Contain("/connect/token");
        doc.RootElement.GetProperty("jwks_uri").GetString().Should().Contain("jwks");
        doc.RootElement.GetProperty("grant_types_supported").EnumerateArray()
            .Select(e => e.GetString())
            .Should().Contain("password");
    }

    [Fact]
    public async Task ConnectToken_ValidCredentials_ReturnsAccessToken()
    {
        var res = await PostTokenAsync("admin", IdentityApiFactory.TestAdminPassword);

        res.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await res.Content.ReadFromJsonAsync<JsonDocument>();
        body!.RootElement.GetProperty("access_token").GetString().Should().NotBeNullOrEmpty();
        body.RootElement.GetProperty("token_type").GetString().Should().BeEquivalentTo("Bearer");
        body.RootElement.GetProperty("expires_in").GetInt32().Should().BePositive();
    }

    [Fact]
    public async Task ConnectToken_IssuedToken_ContainsExpectedClaims()
    {
        var res = await PostTokenAsync("admin", IdentityApiFactory.TestAdminPassword);
        res.EnsureSuccessStatusCode();

        var body = await res.Content.ReadFromJsonAsync<JsonDocument>();
        var accessToken = body!.RootElement.GetProperty("access_token").GetString()!;

        // Parse without validating signature — inspect claims only
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(accessToken);

        jwt.Issuer.Should().Be("http://localhost:5245");
        jwt.Audiences.Should().Contain("dairy-bidding-api");
        jwt.Claims.Should().Contain(c => c.Type == "scope" && c.Value.Contains("bidding.write"));
    }

    [Fact]
    public async Task ConnectToken_InvalidCredentials_Returns400_InvalidGrant()
    {
        var res = await PostTokenAsync("admin", "wrong-password");

        res.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var body = await res.Content.ReadFromJsonAsync<JsonDocument>();
        body!.RootElement.GetProperty("error").GetString().Should().Be("invalid_grant");
    }

    [Fact]
    public async Task ConnectToken_UnknownClient_Returns400_InvalidClient()
    {
        var res = await _client.PostAsync("/connect/token", new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["grant_type"] = "password",
                ["client_id"] = "unknown-client",
                ["username"] = "admin",
                ["password"] = IdentityApiFactory.TestAdminPassword,
                ["scope"] = "bidding.write",
            }));

        res.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var body = await res.Content.ReadFromJsonAsync<JsonDocument>();
        body!.RootElement.GetProperty("error").GetString().Should().Be("invalid_client");
    }

    private Task<HttpResponseMessage> PostTokenAsync(string username, string password)
        => _client.PostAsync("/connect/token", new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["grant_type"] = "password",
                ["client_id"] = "dairy-bidding-web",
                ["username"] = username,
                ["password"] = password,
                ["scope"] = "openid bidding.write",
            }));
}
