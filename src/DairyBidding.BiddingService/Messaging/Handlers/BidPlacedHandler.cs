using DairyBidding.BiddingService.Data;
using DairyBidding.Contracts.Events;
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
            db.AuctionBidReadModels.Add(new AuctionBidReadModel
            {
                AuctionId = evt.AuctionId,
                HighestBidAmount = evt.Amount,
                HighestBidderId = evt.BidderId,
                TotalBids = 1,
                UpdatedAtUtc = DateTime.UtcNow,
            });
        }
        else
        {
            read.TotalBids += 1;
            if (evt.Amount > read.HighestBidAmount)
            {
                read.HighestBidAmount = evt.Amount;
                read.HighestBidderId = evt.BidderId;
            }
            read.UpdatedAtUtc = DateTime.UtcNow;
        }

        db.ProcessedMessages.Add(new ProcessedMessage
        {
            MessageId = evt.MessageId,
            ProcessedAtUtc = DateTime.UtcNow,
        });

        await db.SaveChangesAsync(ct);

        logger.LogInformation("BidPlaced processed. AuctionId={AuctionId}, BidderId={BidderId}, Amount={Amount}",
            evt.AuctionId, evt.BidderId, evt.Amount);
    }
}
