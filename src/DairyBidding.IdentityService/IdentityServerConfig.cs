using Duende.IdentityServer.Models;

namespace DairyBidding.IdentityService;

public static class IdentityServerConfig
{
    public static IEnumerable<IdentityResource> IdentityResources =>
    [
        new IdentityResources.OpenId(),
        new IdentityResources.Profile(),
    ];

    public static IEnumerable<ApiScope> ApiScopes =>
    [
        new ApiScope("bidding.write", "Place and view bids"),
    ];

    public static IEnumerable<ApiResource> ApiResources =>
    [
        new ApiResource("dairy-bidding-api", "Dairy Bidding API")
        {
            Scopes = { "bidding.write" }
        },
    ];

    public static IEnumerable<Client> Clients =>
    [
        new Client
        {
            ClientId = "dairy-bidding-web",
            ClientName = "Dairy Bidding Web App",
            AllowedGrantTypes = GrantTypes.ResourceOwnerPassword,
            RequireClientSecret = false,
            AllowedScopes = { "openid", "profile", "bidding.write" },
            AccessTokenLifetime = 7200, // 2 hours
        },
    ];
}
