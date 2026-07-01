namespace DairyBidding.AuctionService.Messaging;

public record AuctionStatusChangedEvent(
    string MessageId,
    string AuctionId,
    string Title,
    string Status,
    DateTime StartsAt,
    DateTime EndsAt,
    DateTime ChangedAtUtc
);
