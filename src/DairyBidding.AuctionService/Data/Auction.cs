namespace DairyBidding.AuctionService.Data;

public class Auction
{
    public string Id { get; set; } = default!;
    public string Title { get; set; } = default!;
    public string Description { get; set; } = string.Empty;
    public decimal StartingPrice { get; set; }
    public DateTime StartsAt { get; set; }
    public DateTime EndsAt { get; set; }
    public AuctionStatus Status { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    /// <summary>ADR-028: denormalized current leading bid price, updated by BidPlacedEvent consumer.</summary>
    public decimal CurrentPrice { get; set; }
    /// <summary>ADR-028: denormalized total accepted bid count, updated by BidPlacedEvent consumer.</summary>
    public int BidCount { get; set; }
    /// <summary>ADR-022: optimistic concurrency token — incremented on every UPDATE.</summary>
    public int RowVersion { get; set; }
}
