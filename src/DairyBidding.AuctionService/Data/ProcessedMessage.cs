namespace DairyBidding.AuctionService.Data;

public class ProcessedMessage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string MessageId { get; set; } = default!;
    public DateTime ProcessedAtUtc { get; set; }
}
