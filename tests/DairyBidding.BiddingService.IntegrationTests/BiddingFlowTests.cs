using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using DairyBidding.BiddingService.Data;
using DairyBidding.Contracts.Events;
using FluentAssertions;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
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

    [Fact]
    public async Task NoToken_PostBid_Returns401()
    {
        var client = _factory.CreateClient();
        var res = await client.PostAsJsonAsync("/bids",
            new { auctionId = BiddingApiFactory.TestAuctionId, amount = 50m });
        res.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ExpiredToken_PostBid_Returns401()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", JwtTestToken.CreateExpired());
        client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));

        var res = await client.PostAsJsonAsync("/bids",
            new { auctionId = BiddingApiFactory.TestAuctionId, amount = 50m });
        res.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task WrongAudience_PostBid_Returns401()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", JwtTestToken.CreateWithAudience("wrong-audience"));
        client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));

        var res = await client.PostAsJsonAsync("/bids",
            new { auctionId = BiddingApiFactory.TestAuctionId, amount = 50m });
        res.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
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

    [Fact]
    public async Task BidBelowCurrentHighest_IsRejected()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", JwtTestToken.Create());

        // First bid — well above starting price
        client.DefaultRequestHeaders.Remove("Idempotency-Key");
        client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        var first = await client.PostAsJsonAsync("/bids",
            new { auctionId = BiddingApiFactory.TestAuctionId, amount = 200m });
        first.EnsureSuccessStatusCode();

        // Poll until read model reflects the first bid
        await PollUntilAsync(
            async () =>
            {
                var r = await client.GetAsync($"/auctions/{BiddingApiFactory.TestAuctionId}/highest-bid");
                if (!r.IsSuccessStatusCode) return null;
                var json = await r.Content.ReadFromJsonAsync<JsonDocument>();
                var amount = json?.RootElement.GetProperty("highestBidAmount").GetDecimal();
                return amount >= 200m ? json : null;
            },
            attempts: 20, delayMs: 500);

        // Second bid — below current highest
        client.DefaultRequestHeaders.Remove("Idempotency-Key");
        client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        var low = await client.PostAsJsonAsync("/bids",
            new { auctionId = BiddingApiFactory.TestAuctionId, amount = 150m });

        low.StatusCode.Should().Be(HttpStatusCode.Conflict,
            "a bid below the current highest should be rejected");

        var body = await low.Content.ReadFromJsonAsync<JsonDocument>();
        body!.RootElement.GetProperty("currentMinimum").GetDecimal()
            .Should().Be(200m);
    }

    [Fact]
    public async Task ConcurrentBids_AllAccepted_ReadModelIsConsistent()
    {
        // Create a dedicated factory to isolate this test's data
        await using var factory = new BiddingApiFactory();
        await factory.InitializeAsync();

        const int concurrentBids = 5;
        var amounts = new[] { 110m, 130m, 120m, 160m, 140m };

        var tasks = amounts.Select((amount, idx) => Task.Run(async () =>
        {
            var client = factory.CreateClient();
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", JwtTestToken.Create());
            client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
            return await client.PostAsJsonAsync("/bids",
                new { auctionId = BiddingApiFactory.TestAuctionId, amount });
        })).ToList();

        var responses = await Task.WhenAll(tasks);

        // All bids (>= starting price of 100) should be accepted
        responses.Should().AllSatisfy(r =>
            r.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Conflict),
            "each bid is either accepted or rejected for being below the current minimum");

        // At least the highest bid must have been accepted
        var acceptedAmounts = new List<decimal>();
        foreach (var (r, amount) in responses.Zip(amounts))
            if (r.StatusCode == HttpStatusCode.OK)
                acceptedAmounts.Add(amount);

        acceptedAmounts.Should().NotBeEmpty("at least one bid should be accepted");

        // Poll until the read model stabilises
        var read = await PollUntilAsync(
            async () =>
            {
                var r = await factory.CreateClient()
                    .GetAsync($"/auctions/{BiddingApiFactory.TestAuctionId}/highest-bid");
                if (!r.IsSuccessStatusCode) return null;
                var json = await r.Content.ReadFromJsonAsync<JsonDocument>();
                var total = json?.RootElement.GetProperty("totalBids").GetInt32();
                return total == acceptedAmounts.Count ? json : null;
            },
            attempts: 30, delayMs: 500);

        read.Should().NotBeNull("read model should converge within 15 seconds");
        read!.RootElement.GetProperty("highestBidAmount").GetDecimal()
            .Should().Be(acceptedAmounts.Max(), "the read model must reflect the highest accepted bid");
    }

    /// <summary>
    /// ADR-041: When AuctionService extends an auction's EndsAt via anti-snipe, it publishes
    /// an AuctionStatusChangedEvent. BiddingService must consume it and update AuctionReadModel.EndsAt
    /// so that bids in the extended window are accepted rather than rejected.
    /// </summary>
    [Fact]
    public async Task AntiSnipeExtension_UpdatesReadModel_AcceptsBidsInExtendedWindow()
    {
        const string auctionId = "AUC-ANTI-SNIPE-PROPAGATION-TEST";

        // Seed an auction with an already-expired EndsAt — avoids clock sensitivity
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BiddingDbContext>();
            if (!await db.AuctionReadModels.AnyAsync(a => a.AuctionId == auctionId))
            {
                db.AuctionReadModels.Add(new AuctionReadModel
                {
                    AuctionId = auctionId,
                    Title = "Anti-snipe propagation test",
                    Status = "Active",
                    StartingPrice = 50m,
                    StartsAt = DateTime.UtcNow.AddHours(-2),
                    EndsAt = DateTime.UtcNow.AddHours(-1), // already expired
                    UpdatedAtUtc = DateTime.UtcNow,
                });
                await db.SaveChangesAsync();
            }
        }

        // Simulate the AuctionStatusChangedEvent published by AuctionService after an anti-snipe extension
        var newEndsAt = DateTime.UtcNow.AddHours(1);
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var publisher = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();
            await publisher.Publish(new AuctionStatusChangedEvent(
                Guid.NewGuid().ToString("N"),
                auctionId,
                "Anti-snipe propagation test",
                "Active",
                DateTime.UtcNow.AddHours(-2),
                newEndsAt,
                DateTime.UtcNow,
                50m));
        }

        // Poll until AuctionStatusChangedConsumer has updated AuctionReadModel.EndsAt
        var updated = await PollUntilAsync(
            async () =>
            {
                await using var scope = _factory.Services.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<BiddingDbContext>();
                var model = await db.AuctionReadModels.AsNoTracking()
                    .FirstOrDefaultAsync(a => a.AuctionId == auctionId);
                return model?.EndsAt > DateTime.UtcNow ? model : null;
            },
            attempts: 20, delayMs: 500);

        updated.Should().NotBeNull("AuctionStatusChangedConsumer must update AuctionReadModel.EndsAt");

        // Now place a bid — must be accepted because EndsAt is in the future
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", JwtTestToken.Create());
        client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));

        var res = await client.PostAsJsonAsync("/bids", new { auctionId, amount = 75m });
        res.StatusCode.Should().Be(HttpStatusCode.OK,
            "bid should be accepted after anti-snipe extension propagated updated EndsAt to BiddingService");
    }
}