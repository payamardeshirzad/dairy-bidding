namespace DairyBidding.BiddingService.Messaging;

public record BidPlacedEvent(
    Guid BidId,
    string AuctionId,
    string BidderId,
    decimal Amount,
    DateTime CreatedAtUtc
);