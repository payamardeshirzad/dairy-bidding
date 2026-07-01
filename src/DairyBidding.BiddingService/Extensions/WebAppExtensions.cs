using System.Security.Claims;
using DairyBidding.BiddingService.Data;
using DairyBidding.BiddingService.Messaging;
using DairyBidding.Contracts.Events;
using Microsoft.EntityFrameworkCore;

namespace DairyBidding.BiddingService.Extensions;

public static class WebAppExtensions
{
    public static void UseCorrelationId(this IApplicationBuilder app)
    {
        app.Use(async (context, next) =>
        {
            const string header = "X-Correlation-ID";
            var correlationId = context.Request.Headers.TryGetValue(header, out var incoming) &&
                                !string.IsNullOrWhiteSpace(incoming)
                ? incoming.ToString()
                : Guid.NewGuid().ToString("N");

            context.Items[header] = correlationId;
            context.Response.Headers[header] = correlationId;

            var logger = context.RequestServices.GetRequiredService<ILogger<WebApplication>>();
            using (logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlationId }))
            {
                await next();
            }
        });
    }

    public static async Task MigrateAsync(this WebApplication app)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BiddingDbContext>();
        await db.Database.MigrateAsync();
    }

    public static void MapBiddingEndpoints(this WebApplication app)
    {
        app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "bidding" }));
        app.MapHealthChecks("/health/live");
        app.MapHealthChecks("/health/ready");

        app.MapGet("/auctions/{auctionId}/highest-bid", async (string auctionId, BiddingDbContext db, CancellationToken ct) =>
        {
            var read = await db.AuctionBidReadModels
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.AuctionId == auctionId, ct);

            if (read is null)
                return Results.Ok(new
                {
                    AuctionId = auctionId,
                    HighestBidAmount = (decimal?)null,
                    HighestBidderId = (string?)null,
                    TotalBids = 0,
                    UpdatedAtUtc = (DateTime?)null,
                });

            return Results.Ok(new
            {
                read.AuctionId,
                read.HighestBidAmount,
                read.HighestBidderId,
                read.TotalBids,
                read.UpdatedAtUtc,
            });
        });

        app.MapGet("/auctions/{auctionId}/bids", async (string auctionId, BiddingDbContext db, CancellationToken ct) =>
        {
            var bids = await db.Bids
                .AsNoTracking()
                .Where(x => x.AuctionId == auctionId)
                .OrderByDescending(x => x.CreatedAtUtc)
                .Select(x => new { x.Id, x.AuctionId, x.BidderId, x.Amount, x.CreatedAtUtc })
                .ToListAsync(ct);

            return Results.Ok(new { AuctionId = auctionId, Count = bids.Count, Items = bids });
        });

        app.MapPost("/bids", async (
            HttpContext http,
            PlaceBidRequest request,
            ClaimsPrincipal user,
            BiddingDbContext db,
            IBidPlacedPublisher publisher,
            ILogger<WebApplication> logger,
            CancellationToken ct) =>
        {
            var bidderId = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? "unknown";

            if (string.IsNullOrWhiteSpace(request.AuctionId))
                return Results.BadRequest("AuctionId cannot be empty.");

            if (request.Amount <= 0)
                return Results.BadRequest("Amount must be greater than zero.");

            var now = DateTime.UtcNow;
            var auctionState = await db.AuctionReadModels
                .AsNoTracking()
                .FirstOrDefaultAsync(a => a.AuctionId == request.AuctionId, ct);

            if (auctionState is null)
                return Results.NotFound(new { Message = $"Auction '{request.AuctionId}' not found." });

            if (auctionState.Status != "Active" || auctionState.EndsAt < now)
                return Results.Conflict(new { Message = $"Auction '{request.AuctionId}' is not accepting bids." });

            if (!http.Request.Headers.TryGetValue("Idempotency-Key", out var keyValues))
                return Results.BadRequest("Missing Idempotency-Key header.");

            var idempotencyKey = keyValues.ToString().Trim();
            if (string.IsNullOrWhiteSpace(idempotencyKey))
                return Results.BadRequest("Idempotency-Key cannot be empty.");

            var existing = await db.Bids
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.BidderId == bidderId && x.IdempotencyKey == idempotencyKey, ct);

            if (existing is not null)
            {
                return Results.Ok(new
                {
                    existing.Id, existing.AuctionId, existing.BidderId,
                    existing.Amount, existing.CreatedAtUtc, Deduplicated = true,
                });
            }

            var bid = new Bid
            {
                AuctionId = request.AuctionId,
                BidderId = bidderId,
                Amount = request.Amount,
                CreatedAtUtc = DateTime.UtcNow,
                IdempotencyKey = idempotencyKey,
            };

            logger.LogInformation("Placing bid: AuctionId={AuctionId}, BidderId={BidderId}, Amount={Amount}",
                bid.AuctionId, bid.BidderId, bid.Amount);

            db.Bids.Add(bid);
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException)
            {
                var raced = await db.Bids
                    .AsNoTracking()
                    .FirstAsync(x => x.BidderId == bidderId && x.IdempotencyKey == idempotencyKey, ct);

                return Results.Ok(new
                {
                    raced.Id, raced.AuctionId, raced.BidderId,
                    raced.Amount, raced.CreatedAtUtc, Deduplicated = true,
                });
            }

            var evt = new BidPlacedEvent(
                Guid.NewGuid().ToString("N"),
                bid.Id, bid.AuctionId, bid.BidderId, bid.Amount, bid.CreatedAtUtc);

            var correlationId = http.Items["X-Correlation-ID"]?.ToString();
            await publisher.PublishAsync(evt, correlationId, http.RequestAborted);

            return Results.Ok(new
            {
                bid.Id, bid.AuctionId, bid.BidderId,
                bid.Amount, bid.CreatedAtUtc, Deduplicated = false,
            });
        }).RequireAuthorization();

        app.MapPrometheusScrapingEndpoint();
    }
}

record PlaceBidRequest(string AuctionId, decimal Amount);
