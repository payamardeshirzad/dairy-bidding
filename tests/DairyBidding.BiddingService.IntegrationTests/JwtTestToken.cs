using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

public static class JwtTestToken
{
    public static string Create()
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes("THIS_IS_DEV_ONLY_CHANGE_ME_1234567890"));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: "dairy-identity",
            audience: "dairy-bidding-api",
            claims: new[]
            {
                new Claim(ClaimTypes.NameIdentifier, "integration-user-1")
            },
            expires: DateTime.UtcNow.AddMinutes(30),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public static string CreateExpired()
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes("THIS_IS_DEV_ONLY_CHANGE_ME_1234567890"));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: "dairy-identity",
            audience: "dairy-bidding-api",
            claims: new[] { new Claim(ClaimTypes.NameIdentifier, "integration-user-1") },
            expires: DateTime.UtcNow.AddMinutes(-30),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public static string CreateWithAudience(string audience)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes("THIS_IS_DEV_ONLY_CHANGE_ME_1234567890"));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: "dairy-identity",
            audience: audience,
            claims: new[] { new Claim(ClaimTypes.NameIdentifier, "integration-user-1") },
            expires: DateTime.UtcNow.AddMinutes(30),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}