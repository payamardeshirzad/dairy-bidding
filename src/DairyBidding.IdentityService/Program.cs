using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddEndpointsApiExplorer();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var jwtSection = builder.Configuration.GetSection("Jwt");
var issuer = jwtSection.GetValue<string>("Issuer") ?? "dairy-identity";
var audience = jwtSection.GetValue<string>("Audience") ?? "dairy-bidding-api";
var signingKey = jwtSection.GetValue<string>("SigningKey") ?? "THIS_IS_DEV_ONLY_CHANGE_ME_1234567890";
var expiryMinutes = jwtSection.GetValue<int?>("ExpiryMinutes") ?? 120;

var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey));

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = issuer,
            ValidAudience = audience,
            IssuerSigningKey = key,
            ClockSkew = TimeSpan.FromSeconds(30) // Allow a small clock skew for token expiration
        };
    });

builder.Services.AddAuthorization();
var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "identity" }));

app.MapPost("/auth/token", (LoginRequest request) =>
{
    // Dev-only credential check
    if (request.Username != "admin" || request.Password != "admin123")
        return Results.Unauthorized();

    var claims = new List<Claim>
    {
        new(JwtRegisteredClaimNames.Sub, request.Username),
        new(JwtRegisteredClaimNames.UniqueName, request.Username),
        new(ClaimTypes.Name, request.Username),
        new(ClaimTypes.Role, "Admin"),
        new("scope", "bidding.write")
    };

    var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
    var expires = DateTime.UtcNow.AddMinutes(expiryMinutes);

    var token = new JwtSecurityToken(
        issuer: issuer,
        audience: audience,
        claims: claims,
        expires: expires,
        signingCredentials: creds);

    var jwt = new JwtSecurityTokenHandler().WriteToken(token);

    return Results.Ok(new TokenResponse(jwt, "Bearer", expires));
});

app.MapGet("/auth/me", (ClaimsPrincipal user) =>
{
    var name = user.Identity?.Name ?? "unknown";
    var claims = user.Claims.Select(c => new { c.Type, c.Value });
    return Results.Ok(new { name, claims });
}).RequireAuthorization();

app.Run();

record LoginRequest(string Username, string Password);
record TokenResponse(string AccessToken, string TokenType, DateTime ExpiresAtUtc);