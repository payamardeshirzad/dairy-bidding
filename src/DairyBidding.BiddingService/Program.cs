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
builder.Services.Configure<RabbitMqOptions>(builder.Configuration.GetSection("RabbitMQ"));
builder.Services.AddSingleton<IBidPlacedPublisher, BidPlacedPublisher>();
builder.Services.AddHostedService<BidPlacedConsumer>();
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
} else {
    app.UseHttpsRedirection();
}
app.UseAuthentication();
app.UseAuthorization();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<BiddingDbContext>();
    db.Database.Migrate();
}

app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "bidding" }));

app.MapPost("/bids", async (
    HttpContext http,
    PlaceBidRequest request,
    ClaimsPrincipal user,
    BiddingDbContext db,
    IBidPlacedPublisher publisher) =>
{
    var bidderId = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? "unknown";

    if (!http.Request.Headers.TryGetValue("Idempotency-Key", out var keyValues))
        return Results.BadRequest("Missing Idempotency-Key header.");

    var idempotencyKey = keyValues.ToString().Trim();
    if (string.IsNullOrWhiteSpace(idempotencyKey))
        return Results.BadRequest("Idempotency-Key cannot be empty.");

    // 1) Check existing
    var existing = await db.Bids
        .AsNoTracking()
        .FirstOrDefaultAsync(x => x.BidderId == bidderId && x.IdempotencyKey == idempotencyKey);

    if (existing is not null)
    {
        // Already created earlier; do NOT publish again
        return Results.Ok(new
        {
            existing.Id,
            existing.AuctionId,
            existing.BidderId,
            existing.Amount,
            existing.CreatedAtUtc,
            Deduplicated = true
        });
    }

    // 2) Create new bid
    var bid = new Bid
    {
        AuctionId = request.AuctionId,
        BidderId = bidderId,
        Amount = request.Amount,
        CreatedAtUtc = DateTime.UtcNow,
        IdempotencyKey = idempotencyKey
    };
    Console.WriteLine($"Placing bid: AuctionId={bid.AuctionId}, BidderId={bid.BidderId}, Amount={bid.Amount}, CreatedAtUtc={bid.CreatedAtUtc}");
    db.Bids.Add(bid);
    try
    {
        await db.SaveChangesAsync();
    }
    catch (DbUpdateException)
    {
        // Race condition: another request inserted same (BidderId, IdempotencyKey)
        var raced = await db.Bids
            .AsNoTracking()
            .FirstAsync(x => x.BidderId == bidderId && x.IdempotencyKey == idempotencyKey);

        return Results.Ok(new
        {
            raced.Id,
            raced.AuctionId,
            raced.BidderId,
            raced.Amount,
            raced.CreatedAtUtc,
            Deduplicated = true
        });
    }

    // 3) Publish only for newly created bid
    var evt = new BidPlacedEvent(
    Guid.NewGuid().ToString("N"),
    bid.Id,
    bid.AuctionId,
    bid.BidderId,
    bid.Amount,
    bid.CreatedAtUtc
);
    publisher.Publish(evt);

    return Results.Ok(new
    {
        bid.Id,
        bid.AuctionId,
        bid.BidderId,
        bid.Amount,
        bid.CreatedAtUtc,
        Deduplicated = false
    });
}).RequireAuthorization();

app.Run();

record PlaceBidRequest(string AuctionId, decimal Amount);