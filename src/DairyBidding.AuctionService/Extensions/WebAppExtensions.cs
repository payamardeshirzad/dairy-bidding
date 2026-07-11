using DairyBidding.AuctionService.Data;
using DairyBidding.Contracts.Events;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace DairyBidding.AuctionService.Extensions;

public static class WebAppExtensions
{
    public static async Task MigrateAsync(this WebApplication app)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AuctionDbContext>();
        await db.Database.MigrateAsync();
    }

    public static void SeedDevDataOnStarted(this WebApplication app)
    {
        app.Lifetime.ApplicationStarted.Register(() =>
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await using var scope = app.Services.CreateAsyncScope();
                    var db = scope.ServiceProvider.GetRequiredService<AuctionDbContext>();
                    var publishEndpoint = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();
                    var logger = scope.ServiceProvider.GetRequiredService<ILogger<WebApplication>>();

                    var seeds = new[]
                    {
                        new Auction { Id = "AUC-CHEDDAR-001", Title = "Fresh Cheddar Lot A",   Description = "Premium aged cheddar, 50 kg wheel lot.",     StartingPrice = 150m, StartsAt = DateTime.UtcNow.AddMinutes(-30), EndsAt = DateTime.UtcNow.AddHours(8),  Status = AuctionStatus.Active, CreatedAtUtc = DateTime.UtcNow },
                        new Auction { Id = "AUC-GOUDA-001",   Title = "Premium Gouda Wheels",   Description = "Extra-aged Gouda, 12-month maturation.",      StartingPrice = 220m, StartsAt = DateTime.UtcNow.AddMinutes(-10), EndsAt = DateTime.UtcNow.AddHours(12), Status = AuctionStatus.Active, CreatedAtUtc = DateTime.UtcNow },
                        new Auction { Id = "AUC-BUTTER-001",  Title = "Organic Butter Bulk",    Description = "Certified organic butter, 25 kg packs.",      StartingPrice = 80m,  StartsAt = DateTime.UtcNow.AddMinutes(-5),  EndsAt = DateTime.UtcNow.AddHours(6),  Status = AuctionStatus.Active, CreatedAtUtc = DateTime.UtcNow },
                    };

                    foreach (var seed in seeds)
                    {
                        if (!await db.Auctions.AnyAsync(a => a.Id == seed.Id))
                        {
                            db.Auctions.Add(seed);
                            await publishEndpoint.Publish(new AuctionStatusChangedEvent(
                                Guid.NewGuid().ToString("N"), seed.Id, seed.Title,
                                "Active", seed.StartsAt, seed.EndsAt, DateTime.UtcNow, seed.StartingPrice));
                            await db.SaveChangesAsync();
                            logger.LogInformation("Seeded auction {Id}", seed.Id);
                        }
                    }
                }
                catch (Exception ex)
                {
                    app.Services.GetRequiredService<ILogger<WebApplication>>()
                        .LogError(ex, "Dev seed failed.");
                }
            });
        });
    }

    public static void MapAuctionEndpoints(this WebApplication app)
    {
        app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "auction" }));
        app.MapHealthChecks("/health/live");
        app.MapHealthChecks("/health/ready");

        app.MapGet("/auctions/active", async (AuctionDbContext db, CancellationToken ct) =>
        {
            var now = DateTime.UtcNow;
            var auctions = await db.Auctions
                .AsNoTracking()
                .Where(a => a.Status == AuctionStatus.Active && a.StartsAt <= now && a.EndsAt >= now)
                .OrderBy(a => a.EndsAt)
                .Select(a => new
                {
                    a.Id, a.Title, a.Description, a.StartingPrice, a.CurrentPrice, a.BidCount,
                    a.StartsAt, a.EndsAt, Status = a.Status.ToString(),
                })
                .ToListAsync(ct);

            return Results.Ok(auctions);
        });

        app.MapGet("/auctions/{id}", async (string id, AuctionDbContext db, CancellationToken ct) =>
        {
            var auction = await db.Auctions.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, ct);
            if (auction is null)
                return Results.NotFound(new { Message = $"Auction '{id}' not found." });

            return Results.Ok(new
            {
                auction.Id, auction.Title, auction.Description, auction.StartingPrice,
                auction.CurrentPrice, auction.BidCount,
                auction.StartsAt, auction.EndsAt, Status = auction.Status.ToString(),
            });
        });

        app.MapPrometheusScrapingEndpoint();
    }
}
