namespace DairyBidding.BiddingService.Data;

public class Bid
{
    public Guid Id { get; set; }
    public string AuctionId { get; set; } = default!;
    public string BidderId { get; set; } = default!;
    public decimal Amount { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public string IdempotencyKey { get; set; } = default!;
}