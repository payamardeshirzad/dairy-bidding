using System.Security.Claims;
using System.Text;
using DairyBidding.BiddingService.Data;
using DairyBidding.BiddingService.Messaging;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using RabbitMQ.Client;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using OpenTelemetry.Exporter;
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();

// Add services to the container.
builder.Services.Configure<RabbitMqOptions>(builder.Configuration.GetSection("RabbitMQ"));

builder.Services.AddSingleton<BidPlacedPublisher>();
builder.Services.AddSingleton<IBidPlacedPublisher>(sp => sp.GetRequiredService<BidPlacedPublisher>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<BidPlacedPublisher>());

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
var defaultConnection = builder.Configuration.GetConnectionString("Postgres");
if (string.IsNullOrWhiteSpace(defaultConnection))
    throw new InvalidOperationException("ConnectionStrings:Postgres is missing.");

builder.Services
    .AddHealthChecks()
    .AddNpgSql(
        defaultConnection,
        name: "postgres",
        failureStatus: HealthStatus.Unhealthy,
        tags: new[] { "ready" })
    .AddRabbitMQ(
        sp =>
        {
            var cfg = sp.GetRequiredService<IConfiguration>();
            var factory = new ConnectionFactory
            {
                HostName = cfg["RabbitMQ:Host"],
                Port = int.TryParse(cfg["RabbitMQ:Port"], out var p) ? p : 5672,
                UserName = cfg["RabbitMQ:User"],
                Password = cfg["RabbitMQ:Pass"],
                VirtualHost = cfg["RabbitMQ:VHost"] ?? "/"
            };

            return factory.CreateConnectionAsync();
        },
        name: "rabbitmq",
        failureStatus: HealthStatus.Unhealthy,
        tags: new[] { "ready" });

        builder.Services.AddOpenTelemetry()
        .WithTracing(tracing =>
        {
            tracing
                .SetResourceBuilder(
                    ResourceBuilder.CreateDefault()
                        .AddService("dairy-bidding-service"))
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddEntityFrameworkCoreInstrumentation()
                .AddOtlpExporter(otlp =>
                {
                    // Jaeger OTLP gRPC (typical local setup)
                    otlp.Endpoint = new Uri("http://localhost:4317");
                    otlp.Protocol = OtlpExportProtocol.Grpc;
                });
    });
var app = builder.Build();

app.Use(async (context, next) =>
{
    const string header = "X-Correlation-ID";

    var correlationId = context.Request.Headers.TryGetValue(header, out var incoming) &&
                        !string.IsNullOrWhiteSpace(incoming)
        ? incoming.ToString()
        : Guid.NewGuid().ToString("N");

    context.Items[header] = correlationId;
    context.Response.Headers[header] = correlationId;

    using (app.Logger.BeginScope(new Dictionary<string, object>
    {
        ["CorrelationId"] = correlationId
    }))
    {
        await next();
    }
});
// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}
else
{
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
// Highest bid snapshot from read model
app.MapGet("/auctions/{auctionId}/highest-bid", async (string auctionId, BiddingDbContext db) =>
{
    var read = await db.AuctionBidReadModels
        .AsNoTracking()
        .FirstOrDefaultAsync(x => x.AuctionId == auctionId);

    if (read is null)
        return Results.NotFound(new { Message = $"No bids found for auction '{auctionId}'." });

    return Results.Ok(new
    {
        read.AuctionId,
        read.HighestBidAmount,
        read.HighestBidderId,
        read.TotalBids,
        read.UpdatedAtUtc
    });
});

// Bid history from write table
app.MapGet("/auctions/{auctionId}/bids", async (string auctionId, BiddingDbContext db) =>
{
    var bids = await db.Bids
        .AsNoTracking()
        .Where(x => x.AuctionId == auctionId)
        .OrderByDescending(x => x.CreatedAtUtc)
        .Select(x => new
        {
            x.Id,
            x.AuctionId,
            x.BidderId,
            x.Amount,
            x.CreatedAtUtc
        })
        .ToListAsync();

    return Results.Ok(new
    {
        AuctionId = auctionId,
        Count = bids.Count,
        Items = bids
    });
});
app.MapHealthChecks("/health/live");
app.MapHealthChecks("/health/ready");
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
    var correlationId = http.Items["X-Correlation-ID"]?.ToString();
    await publisher.PublishAsync(evt, correlationId, http.RequestAborted);

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
// for WebApplicationFactory in integration tests
public partial class Program { }