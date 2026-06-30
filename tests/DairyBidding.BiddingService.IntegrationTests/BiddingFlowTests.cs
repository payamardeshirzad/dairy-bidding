using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Npgsql;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

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
        var token = JwtTestToken.Create(); // helper below
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        _client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));

        var auctionId = $"AUC-{Guid.NewGuid():N}";
        var payload = new { auctionId, amount = 120.50m };

        var res = await _client.PostAsJsonAsync("/bids", payload);
        res.EnsureSuccessStatusCode();

        // eventual consistency wait
        var ok = false;
        for (var i = 0; i < 20; i++)
        {
            await Task.Delay(500);
            var read = await _client.GetAsync($"/auctions/{auctionId}/highest-bid");
            if (read.IsSuccessStatusCode)
            {
                ok = true;
                break;
            }
        }

        ok.Should().BeTrue("read model should be updated by consumer");
    }

    [Fact]
    public async Task DuplicateMessage_IsIgnored_ByIdempotency()
    {
        var token = JwtTestToken.Create();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var idem = Guid.NewGuid().ToString("N");
        var auctionId = $"AUC-{Guid.NewGuid():N}";
        var payload = new { auctionId, amount = 99m };

        _client.DefaultRequestHeaders.Remove("Idempotency-Key");
        _client.DefaultRequestHeaders.Add("Idempotency-Key", idem);
        var r1 = await _client.PostAsJsonAsync("/bids", payload);
        r1.EnsureSuccessStatusCode();

        _client.DefaultRequestHeaders.Remove("Idempotency-Key");
        _client.DefaultRequestHeaders.Add("Idempotency-Key", idem);
        var r2 = await _client.PostAsJsonAsync("/bids", payload);
        r2.EnsureSuccessStatusCode();

        var body2 = await r2.Content.ReadAsStringAsync();
        body2.Should().Contain("\"deduplicated\":true", because: "second request should be deduplicated");    
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
            VirtualHost = _factory.RabbitVHost
        };

        await using var conn = await factory.CreateConnectionAsync();
        await using var ch = await conn.CreateChannelAsync();

        var bad = Encoding.UTF8.GetBytes("{\"not\":\"valid BidPlacedEvent\"}");

        await ch.BasicPublishAsync(
            exchange: "bidding.events",
            routingKey: "bid.placed",
            mandatory: false,
            basicProperties: new BasicProperties { Persistent = true, ContentType = "application/json" },
            body: bad);

        // wait for retries + dlq
        await Task.Delay(TimeSpan.FromSeconds(20));

        var result = await ch.QueueDeclarePassiveAsync("bidding.bidplaced.dlq");
        result.MessageCount.Should().BeGreaterThan(0);
    }
}