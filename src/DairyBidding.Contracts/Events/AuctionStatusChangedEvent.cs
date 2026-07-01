namespace DairyBidding.Contracts.Events;

public record AuctionStatusChangedEvent(
    string MessageId,
    string AuctionId,
    string Title,
    string Status,
    DateTime StartsAt,
    DateTime EndsAt,
    DateTime ChangedAtUtc
);
