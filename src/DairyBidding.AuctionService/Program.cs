using System.Text;
using DairyBidding.AuctionService.Data;
using DairyBidding.AuctionService.Messaging;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.IdentityModel.Tokens;
using OpenTelemetry.Exporter;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using RabbitMQ.Client;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApi();

builder.Services.Configure<RabbitMqOptions>(builder.Configuration.GetSection("RabbitMQ"));
builder.Services.AddSingleton<AuctionEventPublisher>();
builder.Services.AddSingleton<IAuctionEventPublisher>(sp => sp.GetRequiredService<AuctionEventPublisher>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<AuctionEventPublisher>());

builder.Services.AddDbContext<AuctionDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Postgres")));

var jwtSection = builder.Configuration.GetSection("Jwt");
var issuer = jwtSection["Issuer"] ?? "dairy-identity";
var audience = jwtSection["Audience"] ?? "dairy-bidding-api";
var signingKey = jwtSection["SigningKey"] ?? "THIS_IS_DEV_ONLY_CHANGE_ME_1234567890";
var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey));

var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? Array.Empty<string>();
builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy =>
        policy.WithOrigins(allowedOrigins)
              .WithMethods("GET", "POST", "OPTIONS")
              .WithHeaders("Authorization", "Content-Type"));
});

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
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

builder.Services.AddHealthChecks()
    .AddNpgSql(defaultConnection, name: "postgres", failureStatus: HealthStatus.Unhealthy, tags: new[] { "ready" })
    .AddRabbitMQ(sp =>
    {
        var cfg = sp.GetRequiredService<IConfiguration>();
        var factory = new ConnectionFactory
        {
            HostName = cfg["RabbitMQ:Host"] ?? "localhost",
            Port = int.TryParse(cfg["RabbitMQ:Port"], out var p) ? p : 5672,
            UserName = cfg["RabbitMQ:User"] ?? "guest",
            Password = cfg["RabbitMQ:Pass"] ?? "guest",
            VirtualHost = cfg["RabbitMQ:VHost"] ?? "/"
        };
        return factory.CreateConnectionAsync();
    }, name: "rabbitmq", failureStatus: HealthStatus.Unhealthy, tags: new[] { "ready" });

builder.Services.AddOpenTelemetry()
    .WithTracing(tracing =>
    {
        tracing
            .SetResourceBuilder(ResourceBuilder.CreateDefault().AddService("dairy-auction-service"))
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddEntityFrameworkCoreInstrumentation()
            .AddOtlpExporter(otlp =>
            {
                otlp.Endpoint = new Uri(builder.Configuration["OpenTelemetry:OtlpEndpoint"] ?? "http://localhost:4317");
                otlp.Protocol = OtlpExportProtocol.Grpc;
            });
    });

var app = builder.Build();

app.UseCors("Frontend");
if (app.Environment.IsDevelopment()) app.MapOpenApi();
else app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();

// Run migrations
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AuctionDbContext>();
    db.Database.Migrate();
}

// Dev seed runs after ApplicationStarted so the AuctionEventPublisher hosted service is fully ready.
if (app.Environment.IsDevelopment())
{
    app.Lifetime.ApplicationStarted.Register(() =>
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await using var scope = app.Services.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<AuctionDbContext>();
                var publisher = scope.ServiceProvider.GetRequiredService<IAuctionEventPublisher>();
                var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

                var seeds = new[]
                {
                    new Auction { Id = "AUC-CHEDDAR-001", Title = "Fresh Cheddar Lot A", Description = "Premium aged cheddar, 50 kg wheel lot.", StartingPrice = 150m, StartsAt = DateTime.UtcNow.AddMinutes(-30), EndsAt = DateTime.UtcNow.AddHours(8),  Status = AuctionStatus.Active, CreatedAtUtc = DateTime.UtcNow },
                    new Auction { Id = "AUC-GOUDA-001",   Title = "Premium Gouda Wheels", Description = "Extra-aged Gouda, 12-month maturation.",  StartingPrice = 220m, StartsAt = DateTime.UtcNow.AddMinutes(-10), EndsAt = DateTime.UtcNow.AddHours(12), Status = AuctionStatus.Active, CreatedAtUtc = DateTime.UtcNow },
                    new Auction { Id = "AUC-BUTTER-001",  Title = "Organic Butter Bulk",  Description = "Certified organic butter, 25 kg packs.",  StartingPrice = 80m,  StartsAt = DateTime.UtcNow.AddMinutes(-5),  EndsAt = DateTime.UtcNow.AddHours(6),  Status = AuctionStatus.Active, CreatedAtUtc = DateTime.UtcNow },
                };

                foreach (var seed in seeds)
                {
                    if (!await db.Auctions.AnyAsync(a => a.Id == seed.Id))
                    {
                        db.Auctions.Add(seed);
                        await db.SaveChangesAsync();
                        await publisher.PublishStatusChangedAsync(new AuctionStatusChangedEvent(
                            Guid.NewGuid().ToString("N"), seed.Id, seed.Title,
                            "Active", seed.StartsAt, seed.EndsAt, DateTime.UtcNow));
                        logger.LogInformation("Seeded auction {Id}", seed.Id);
                    }
                }
            }
            catch (Exception ex)
            {
                app.Services.GetRequiredService<ILogger<Program>>()
                    .LogError(ex, "Dev seed failed.");
            }
        });
    });
}

app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "auction" }));
app.MapHealthChecks("/health/live");
app.MapHealthChecks("/health/ready");

app.MapGet("/auctions/active", async (AuctionDbContext db) =>
{
    var now = DateTime.UtcNow;
    var auctions = await db.Auctions
        .AsNoTracking()
        .Where(a => a.Status == AuctionStatus.Active && a.StartsAt <= now && a.EndsAt >= now)
        .OrderBy(a => a.EndsAt)
        .Select(a => new
        {
            a.Id,
            a.Title,
            a.Description,
            a.StartingPrice,
            a.StartsAt,
            a.EndsAt,
            Status = a.Status.ToString()
        })
        .ToListAsync();

    return Results.Ok(auctions);
});

app.MapGet("/auctions/{id}", async (string id, AuctionDbContext db) =>
{
    var auction = await db.Auctions.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id);
    if (auction is null)
        return Results.NotFound(new { Message = $"Auction '{id}' not found." });

    return Results.Ok(new
    {
        auction.Id,
        auction.Title,
        auction.Description,
        auction.StartingPrice,
        auction.StartsAt,
        auction.EndsAt,
        Status = auction.Status.ToString()
    });
});

app.Run();
