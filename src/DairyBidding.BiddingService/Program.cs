using System.Security.Claims;
using System.Text;
using DairyBidding.BiddingService.Data;
using DairyBidding.BiddingService.Messaging;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();

// Add services to the container.
builder.Services.AddSingleton<IBidPlacedPublisher, BidPlacedPublisher>();

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
builder.Services.Configure<RabbitMqOptions>(
    builder.Configuration.GetSection("RabbitMQ"));
var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
} else {
    app.UseHttpsRedirection();
}
app.UseAuthentication();
app.UseAuthorization();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<BiddingDbContext>();
    db.Database.EnsureCreated();
}

app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "bidding" }));

app.MapPost("/bids", async (
    PlaceBidRequest request,
    ClaimsPrincipal user,
    BiddingDbContext db,
    IBidPlacedPublisher publisher) =>
{
    var bidder = user.Identity?.Name ?? user.FindFirstValue(ClaimTypes.Name) ?? "unknown";

    var bid = new Bid
    {
        AuctionId = request.AuctionId,
        BidderId = bidder,
        Amount = request.Amount,
        CreatedAtUtc = DateTime.UtcNow
    };
    Console.WriteLine($"Placing bid: AuctionId={bid.AuctionId}, BidderId={bid.BidderId}, Amount={bid.Amount}, CreatedAtUtc={bid.CreatedAtUtc}");
    db.Bids.Add(bid);
    await db.SaveChangesAsync();

    publisher.Publish(new BidPlacedEvent(
        bid.Id,
        bid.AuctionId,
        bid.BidderId,
        bid.Amount,
        bid.CreatedAtUtc));

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