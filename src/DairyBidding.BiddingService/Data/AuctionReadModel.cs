namespace DairyBidding.BiddingService.Data;

public class AuctionReadModel
{
    public string AuctionId { get; set; } = default!;
    public string Title { get; set; } = default!;
    public string Status { get; set; } = default!;
    public decimal StartingPrice { get; set; }
    public DateTime StartsAt { get; set; }
    public DateTime EndsAt { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}
