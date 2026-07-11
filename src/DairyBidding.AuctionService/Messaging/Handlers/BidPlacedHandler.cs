using DairyBidding.AuctionService.Data;
using DairyBidding.Contracts.Events;
using DairyBidding.SharedKernel;
using DairyBidding.SharedKernel.Messaging;
using Microsoft.EntityFrameworkCore;

namespace DairyBidding.AuctionService.Messaging.Handlers;

public sealed class BidPlacedHandler(AuctionDbContext db, ILogger<BidPlacedHandler> logger)
    : IMessageHandler<BidPlacedEvent>
{
    public async Task HandleAsync(BidPlacedEvent evt, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(evt.MessageId))
            throw new InvalidOperationException("MessageId is required for idempotency.");

        var alreadyProcessed = await db.ProcessedMessages
            .AnyAsync(x => x.MessageId == evt.MessageId, ct);

        if (alreadyProcessed)
        {
            logger.LogInformation("Duplicate BidPlaced skipped. MessageId={MessageId}", evt.MessageId);
            return;
        }

        var auction = await db.Auctions
            .FirstOrDefaultAsync(a => a.Id == evt.AuctionId, ct);

        if (auction is null)
        {
            // Auction may not be synced to this service yet (transient ordering issue).
            // Throw so MassTransit retries; the auction_db should be consistent shortly.
            throw new InvalidOperationException(
                $"BidPlaced: Auction '{evt.AuctionId}' not found in auction_db. Will retry.");
        }

        // ProcessedMessage is added once, outside the retry loop.
        // It remains in Added state across concurrency retries and is committed
        // atomically on the first successful SaveChangesAsync.
        db.ProcessedMessages.Add(new ProcessedMessage
        {
            MessageId = evt.MessageId,
            ProcessedAtUtc = DateTime.UtcNow,
        });

        await OptimisticRetry.ExecuteAsync(
            maxAttempts: OptimisticRetry.DefaultMaxAttempts,
            baseDelayMs: OptimisticRetry.DefaultBaseDelayMs,
            logger: logger,
            ct: ct,
            action: async () =>
            {
                // OptimisticRetry calls entry.ReloadAsync() after each conflict,
                // refreshing `auction` in-place to the current DB values.
                // Re-applying the delta here produces the correct next state.
                if (evt.Amount > auction.CurrentPrice)
                    auction.CurrentPrice = evt.Amount;
                auction.BidCount++;
                auction.RowVersion++;
                await db.SaveChangesAsync(ct);
            });

        logger.LogInformation(
            "BidPlaced applied. AuctionId={AuctionId}, BidCount={BidCount}, CurrentPrice={CurrentPrice}",
            evt.AuctionId, auction.BidCount, auction.CurrentPrice);
    }
}
