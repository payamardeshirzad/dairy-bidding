using DairyBidding.AuctionService.Data;
using DairyBidding.Contracts.Events;
using DairyBidding.SharedKernel;
using DairyBidding.SharedKernel.Messaging;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DairyBidding.AuctionService.Messaging.Handlers;

public sealed class BidPlacedHandler(
    AuctionDbContext db,
    IPublishEndpoint publishEndpoint,
    IOptions<AntiSnipeOptions> antiSnipeOptions,
    ILogger<BidPlacedHandler> logger)
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

        var opts = antiSnipeOptions.Value;
        var antiSnipeWindow = TimeSpan.FromMinutes(opts.WindowMinutes);
        var extensionDuration = TimeSpan.FromMinutes(opts.ExtensionMinutes);

        // Declared outside the lambda so the entity reference survives across retries
        // without being re-added to the change tracker on each attempt. See ADR-034.
        AuctionExtension? pendingExtension = null;

        await OptimisticRetry.ExecuteAsync(
            maxAttempts: OptimisticRetry.DefaultMaxAttempts,
            baseDelayMs: OptimisticRetry.DefaultBaseDelayMs,
            logger: logger,
            ct: ct,
            action: async () =>
            {
                // OptimisticRetry calls entry.ReloadAsync() after each conflict,
                // refreshing `auction` in-place to the current DB values.
                // Re-applying every delta here produces the correct next state.
                if (evt.Amount > auction.CurrentPrice)
                    auction.CurrentPrice = evt.Amount;
                auction.BidCount++;
                auction.RowVersion++;

                // ADR-034/040: Anti-snipe extension.
                // Uses refreshed auction.EndsAt (post-ReloadAsync) so a concurrent bid that
                // already extended the window is visible on retry — preventing double extension.
                var timeToClose = auction.EndsAt - evt.CreatedAtUtc;
                if (timeToClose > TimeSpan.Zero
                    && timeToClose <= antiSnipeWindow
                    && auction.ExtensionCount < opts.MaxExtensionsPerAuction)
                {
                    var previousEnd = auction.EndsAt;
                    var newEnd = previousEnd.Add(extensionDuration);
                    auction.EndsAt = newEnd;
                    auction.ExtensionCount++;

                    if (pendingExtension is null)
                    {
                        pendingExtension = new AuctionExtension
                        {
                            Id = Guid.NewGuid(),
                            AuctionId = evt.AuctionId,
                            BidId = evt.BidId,
                            PreviousEnd = previousEnd,
                            NewEnd = newEnd,
                            ExtendedAt = DateTime.UtcNow,
                        };
                        db.AuctionExtensions.Add(pendingExtension);
                    }
                    else
                    {
                        // Retry: entity is already in Added state; update values in place.
                        pendingExtension.PreviousEnd = previousEnd;
                        pendingExtension.NewEnd = newEnd;
                    }
                }
                else if (pendingExtension is not null)
                {
                    // ReloadAsync showed a concurrent bid already extended EndsAt beyond
                    // the window, or the extension cap was reached. Discard the pending row.
                    db.Entry(pendingExtension).State = EntityState.Detached;
                    pendingExtension = null;
                    // ExtensionCount was incremented above and then reset by ReloadAsync —
                    // the refreshed value is already correct; no manual decrement needed.
                }

                await db.SaveChangesAsync(ct);
            });

        // ADR-041: Propagate updated EndsAt to BiddingService via AuctionStatusChangedEvent.
        // Published after the retry succeeds to avoid double-queuing outbox rows on retry.
        // Second SaveChangesAsync commits only the outbox row.
        if (pendingExtension is not null)
        {
            await publishEndpoint.Publish(new AuctionStatusChangedEvent(
                Guid.NewGuid().ToString("N"),
                auction.Id,
                auction.Title,
                auction.Status.ToString(),
                auction.StartsAt,
                auction.EndsAt,
                DateTime.UtcNow,
                auction.StartingPrice), ct);

            await db.SaveChangesAsync(ct);
        }

        logger.LogInformation(
            "BidPlaced applied. AuctionId={AuctionId}, BidCount={BidCount}, CurrentPrice={CurrentPrice}, EndsAt={EndsAt}, ExtensionCount={ExtensionCount}",
            evt.AuctionId, auction.BidCount, auction.CurrentPrice, auction.EndsAt, auction.ExtensionCount);
    }
}
