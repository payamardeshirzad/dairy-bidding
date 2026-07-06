using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using RabbitMQ.Client;

public class BiddingFlowTests : IClassFixture<BiddingApiFactory>
{
    private readonly BiddingApiFactory _factory;
    private readonly HttpClient _client;

    public BiddingFlowTests(BiddingApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task HappyPath_PlaceBid_Published_Consumed_ReadModelUpdated()
    {
        var token = JwtTestToken.Create();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        _client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));

        var payload = new { auctionId = BiddingApiFactory.TestAuctionId, amount = 120.50m };

        var res = await _client.PostAsJsonAsync("/bids", payload);
        res.EnsureSuccessStatusCode();

        // Poll until read model reflects the bid (eventual consistency)
        var read = await PollUntilAsync(
            async () =>
            {
                var r = await _client.GetAsync($"/auctions/{BiddingApiFactory.TestAuctionId}/highest-bid");
                if (!r.IsSuccessStatusCode) return null;
                var json = await r.Content.ReadFromJsonAsync<JsonDocument>();
                var amount = json?.RootElement.GetProperty("highestBidAmount").GetDecimal();
                return amount > 0 ? json : null;
            },
            attempts: 20, delayMs: 500);

        read.Should().NotBeNull("read model should be updated by consumer within 10 seconds");
    }

    [Fact]
    public async Task DuplicateMessage_IsIgnored_ByIdempotency()
    {
        var token = JwtTestToken.Create();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var idem = Guid.NewGuid().ToString("N");
        var payload = new { auctionId = BiddingApiFactory.TestAuctionId, amount = 99m };

        _client.DefaultRequestHeaders.Remove("Idempotency-Key");
        _client.DefaultRequestHeaders.Add("Idempotency-Key", idem);
        var r1 = await _client.PostAsJsonAsync("/bids", payload);
        r1.EnsureSuccessStatusCode();

        _client.DefaultRequestHeaders.Remove("Idempotency-Key");
        _client.DefaultRequestHeaders.Add("Idempotency-Key", idem);
        var r2 = await _client.PostAsJsonAsync("/bids", payload);
        r2.EnsureSuccessStatusCode();

        var body2 = await r2.Content.ReadFromJsonAsync<JsonDocument>();
        body2!.RootElement.GetProperty("deduplicated").GetBoolean()
            .Should().BeTrue("second request should be deduplicated");
    }

    [Fact]
    public async Task FailurePath_BadPayload_GoesToDlq()
    {
        var factory = new ConnectionFactory
        {
            HostName = _factory.RabbitHost,
            Port = _factory.RabbitPort,
            UserName = _factory.RabbitUser,
            Password = _factory.RabbitPass,
            VirtualHost = _factory.RabbitVHost,
        };

        await using var conn = await factory.CreateConnectionAsync();
        await using var ch = await conn.CreateChannelAsync();

        var bad = Encoding.UTF8.GetBytes("{\"not\":\"valid BidPlacedEvent\"}");

        await ch.BasicPublishAsync(
            exchange: "",
            routingKey: "bidding.bid-placed",
            mandatory: false,
            basicProperties: new BasicProperties { Persistent = true, ContentType = "application/json" },
            body: bad);

        // wait for retries (3x 500ms intervals) + error queue delivery
        await Task.Delay(TimeSpan.FromSeconds(10));

        var result = await ch.QueueDeclarePassiveAsync("bidding.bid-placed_error");
        result.MessageCount.Should().BeGreaterThan(0);
    }

    private static async Task<T?> PollUntilAsync<T>(Func<Task<T?>> check, int attempts, int delayMs) where T : class
    {
        for (var i = 0; i < attempts; i++)
        {
            await Task.Delay(delayMs);
            var result = await check();
            if (result is not null) return result;
        }
        return null;
    }
}