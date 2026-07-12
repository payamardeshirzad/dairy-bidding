namespace DairyBidding.AuctionService.Data;

/// <summary>ADR-034: Immutable record of each anti-snipe extension applied to an auction.</summary>
public class AuctionExtension
{
    public Guid Id { get; set; }

    /// <summary>Matches <see cref="Auction.Id"/> — string type, no FK constraint per ADR-023.</summary>
    public string AuctionId { get; set; } = default!;

    /// <summary>The bid that triggered the extension.</summary>
    public Guid BidId { get; set; }

    public DateTime PreviousEnd { get; set; }
    public DateTime NewEnd { get; set; }
    public DateTime ExtendedAt { get; set; }
}
