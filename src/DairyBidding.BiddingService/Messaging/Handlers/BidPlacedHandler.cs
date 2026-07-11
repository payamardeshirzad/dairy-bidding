using DairyBidding.BiddingService.Data;
using DairyBidding.Contracts.Events;
using DairyBidding.SharedKernel;
using DairyBidding.SharedKernel.Messaging;
using Microsoft.EntityFrameworkCore;

namespace DairyBidding.BiddingService.Messaging.Handlers;

public sealed class BidPlacedHandler(BiddingDbContext db, ILogger<BidPlacedHandler> logger)
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

        var read = await db.AuctionBidReadModels
            .FirstOrDefaultAsync(x => x.AuctionId == evt.AuctionId, ct);

        if (read is null)
        {
            // INSERT path: first bid for this auction — no concurrency conflict on INSERT.
            // A unique-constraint violation from a race to insert will be retried by MassTransit.
            db.AuctionBidReadModels.Add(new AuctionBidReadModel
            {
                AuctionId = evt.AuctionId,
                HighestBidAmount = evt.Amount,
                HighestBidderId = evt.BidderId,
                TotalBids = 1,
                UpdatedAtUtc = DateTime.UtcNow,
            });
            db.ProcessedMessages.Add(new ProcessedMessage { MessageId = evt.MessageId, ProcessedAtUtc = DateTime.UtcNow });
            await db.SaveChangesAsync(ct);
        }
        else
        {
            // UPDATE path: concurrent writes to the same AuctionBidReadModel are possible.
            // ProcessedMessage is added once outside the retry; it stays in Added state
            // across attempts and is committed atomically on the first successful SaveChangesAsync.
            db.ProcessedMessages.Add(new ProcessedMessage { MessageId = evt.MessageId, ProcessedAtUtc = DateTime.UtcNow });

            await OptimisticRetry.ExecuteAsync(
                maxAttempts: OptimisticRetry.DefaultMaxAttempts,
                baseDelayMs: OptimisticRetry.DefaultBaseDelayMs,
                logger: logger,
                ct: ct,
                action: async () =>
                {
                    // After a DbUpdateConcurrencyException, OptimisticRetry calls
                    // entry.ReloadAsync() which refreshes `read` in-place to current DB values.
                    // Re-applying the delta here produces the correct next state.
                    read.TotalBids++;
                    if (evt.Amount > read.HighestBidAmount)
                    {
                        read.HighestBidAmount = evt.Amount;
                        read.HighestBidderId = evt.BidderId;
                    }
                    read.RowVersion++;
                    read.UpdatedAtUtc = DateTime.UtcNow;
                    await db.SaveChangesAsync(ct);
                });
        }

        logger.LogInformation(
            "BidPlaced processed. AuctionId={AuctionId}, BidderId={BidderId}, Amount={Amount}",
            evt.AuctionId, evt.BidderId, evt.Amount);
    }
}
