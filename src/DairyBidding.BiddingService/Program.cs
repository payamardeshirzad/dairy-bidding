using System.Security.Claims;
using System.Text;
using DairyBidding.BiddingService.Data;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

builder.Services.AddDbContext<BiddingDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Postgres")));

var jwtSection = builder.Configuration.GetSection("Jwt");
var issuer = jwtSection["Issuer"] ?? "dairy-identity";
var audience = jwtSection["Audience"] ?? "dairy-bidding-api";
var signingKey = jwtSection["SigningKey"] ?? "THIS_IS_DEV_ONLY_CHANGE_ME_1234567890";

var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey));

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = issuer,
            ValidateAudience = true,
            ValidAudience = audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = key,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30)
        };
    });

builder.Services.AddAuthorization();
var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

//app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<BiddingDbContext>();
    db.Database.EnsureCreated();
    Console.WriteLine(db.Database.GetConnectionString());
    Console.WriteLine("EnsureCreated executed.");

}

app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "bidding" }));

app.MapGet("/auctions/active", () =>
{
    var auctions = new[]
    {
        new { auctionId = "AUC-1001", title = "Cheddar Batch 12", currentPrice = 1200.00m, currency = "USD" },
        new { auctionId = "AUC-1002", title = "Gouda Reserve Lot", currentPrice = 980.50m, currency = "USD" }
    };
    return Results.Ok(auctions);
});

app.MapPost("/bids", async (PlaceBidRequest request, ClaimsPrincipal user, BiddingDbContext db) =>
{
    var bidder = user.Identity?.Name ?? user.FindFirstValue(ClaimTypes.Name) ?? "unknown";

    var bid = new Bid
    {
        AuctionId = request.AuctionId,
        BidderId = bidder,
        Amount = request.Amount,
        CreatedAtUtc = DateTime.UtcNow
    };

    db.Bids.Add(bid);
    await db.SaveChangesAsync();

    return Results.Ok(new
    {
        bid.Id,
        bid.AuctionId,
        bid.BidderId,
        bid.Amount,
        bid.CreatedAtUtc
    });
}).RequireAuthorization();

app.Run();

record PlaceBidRequest(string AuctionId, decimal Amount);