using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace DairyBidding.IdentityService.Services;

public sealed class TokenService(IConfiguration configuration) : ITokenService
{
    private readonly string _issuer = configuration["Jwt:Issuer"] ?? "dairy-identity";
    private readonly string _audience = configuration["Jwt:Audience"] ?? "dairy-bidding-api";
    private readonly int _expiryMinutes = configuration.GetValue<int?>("Jwt:ExpiryMinutes") ?? 120;
    private readonly SymmetricSecurityKey _key = new(
        Encoding.UTF8.GetBytes(
            configuration["Jwt:SigningKey"]
                ?? throw new InvalidOperationException("Jwt:SigningKey is required. Set it via dotnet user-secrets.")));

    public (string Token, DateTime ExpiresAtUtc) CreateToken(string username, string role)
    {
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, username),
            new Claim(JwtRegisteredClaimNames.UniqueName, username),
            new Claim(ClaimTypes.Name, username),
            new Claim(ClaimTypes.Role, role),
            new Claim("scope", "bidding.write"),
        };

        var creds = new SigningCredentials(_key, SecurityAlgorithms.HmacSha256);
        var expires = DateTime.UtcNow.AddMinutes(_expiryMinutes);

        var token = new JwtSecurityToken(
            issuer: _issuer,
            audience: _audience,
            claims: claims,
            expires: expires,
            signingCredentials: creds);

        return (new JwtSecurityTokenHandler().WriteToken(token), expires);
    }
}
