using DairyBidding.BiddingService.Data;
using DairyBidding.Contracts.Events;
using DairyBidding.SharedKernel.Messaging;
using Microsoft.EntityFrameworkCore;

namespace DairyBidding.BiddingService.Messaging.Handlers;

public sealed class AuctionStatusChangedHandler(BiddingDbContext db, ILogger<AuctionStatusChangedHandler> logger)
    : IMessageHandler<AuctionStatusChangedEvent>
{
    public async Task HandleAsync(AuctionStatusChangedEvent evt, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(evt.MessageId))
            throw new InvalidOperationException("MessageId is required for idempotency.");

        var alreadyProcessed = await db.ProcessedMessages
            .AnyAsync(x => x.MessageId == evt.MessageId, ct);

        if (alreadyProcessed)
        {
            logger.LogInformation("Duplicate AuctionStatusChanged skipped. MessageId={MessageId}", evt.MessageId);
            return;
        }

        var existing = await db.AuctionReadModels
            .FirstOrDefaultAsync(a => a.AuctionId == evt.AuctionId, ct);

        if (existing is null)
        {
            db.AuctionReadModels.Add(new AuctionReadModel
            {
                AuctionId = evt.AuctionId,
                Title = evt.Title,
                Status = evt.Status,
                StartsAt = evt.StartsAt,
                EndsAt = evt.EndsAt,
                UpdatedAtUtc = evt.ChangedAtUtc,
            });
        }
        else
        {
            existing.Status = evt.Status;
            existing.Title = evt.Title;
            existing.StartsAt = evt.StartsAt;
            existing.EndsAt = evt.EndsAt;
            existing.UpdatedAtUtc = evt.ChangedAtUtc;
        }

        db.ProcessedMessages.Add(new ProcessedMessage
        {
            MessageId = evt.MessageId,
            ProcessedAtUtc = DateTime.UtcNow,
        });

        await db.SaveChangesAsync(ct);

        logger.LogInformation("AuctionStatusChanged processed. AuctionId={AuctionId}, Status={Status}",
            evt.AuctionId, evt.Status);
    }
}
