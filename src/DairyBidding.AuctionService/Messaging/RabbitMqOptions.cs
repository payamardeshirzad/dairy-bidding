namespace DairyBidding.AuctionService.Messaging;

public class RabbitMqOptions
{
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 5672;
    public string User { get; set; } = "dairy";
    public string Pass { get; set; } = "dairy_local_pass";
    public string VHost { get; set; } = "dairy-bidding";
}
