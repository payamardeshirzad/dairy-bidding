namespace DairyBidding.BiddingService.Data;

public class ProcessedMessage
{
    public int Id { get; set; }
    public string MessageId { get; set; } = default!;
    public DateTime ProcessedAtUtc { get; set; }
}