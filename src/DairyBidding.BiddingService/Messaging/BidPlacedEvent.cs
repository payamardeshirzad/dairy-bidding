namespace DairyBidding.BiddingService.Messaging;

public record BidPlacedEvent(
    string MessageId,
    Guid BidId,
    string AuctionId,
    string BidderId,
    decimal Amount,
    DateTime CreatedAtUtc
);