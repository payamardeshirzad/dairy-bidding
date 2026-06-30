namespace DairyBidding.BiddingService.Data;

public class AuctionBidReadModel
{
    public string AuctionId { get; set; } = default!;
    public decimal HighestBidAmount { get; set; }
    public string HighestBidderId { get; set; } = default!;
    public int TotalBids { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}