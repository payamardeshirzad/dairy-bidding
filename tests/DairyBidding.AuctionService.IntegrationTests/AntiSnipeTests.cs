using DairyBidding.AuctionService.Data;
using DairyBidding.Contracts.Events;
using FluentAssertions;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RabbitMQ.Client;

/// <summary>
/// Integration tests covering ADR-034 (auction_extensions table), ADR-040 (max extension cap),
/// and ADR-041 (AuctionStatusChangedEvent propagation on extension).
/// </summary>
[Collection("AuctionApiFactory")]
public class AntiSnipeTests : IClassFixture<AuctionApiFactory>
{
    private readonly AuctionApiFactory _factory;

    public AntiSnipeTests(AuctionApiFactory factory)
    {
        _factory = factory;
    }

    // ---------------------------------------------------------------------------
    // ADR-034: extension row written, EndsAt updated
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task BidInAntiSnipeWindow_ExtendsEndsAt_RecordsExtensionRow()
    {
        var endsAt = DateTime.UtcNow.AddMinutes(10);
        await using var setupScope = _factory.Services.CreateAsyncScope();
        var (originalEndsAt, _) = await _factory.ResetTestAuctionAsync(
            setupScope.ServiceProvider.GetRequiredService<AuctionDbContext>(), endsAt);

        // Bid 3 min before close — inside the 5-min anti-snipe window
        await PublishBidAsync(originalEndsAt.AddMinutes(-3), 200m);

        var extension = await PollForExtensionAsync(expectedCount: 1);
        extension.Should().NotBeNull("a bid within the anti-snipe window must trigger an extension");

        await using var verifyScope = _factory.Services.CreateAsyncScope();
        var db = verifyScope.ServiceProvider.GetRequiredService<AuctionDbContext>();
        var auction = await db.Auctions.AsNoTracking().FirstAsync(a => a.Id == AuctionApiFactory.TestAuctionId);

        auction.EndsAt.Should().BeCloseTo(originalEndsAt.AddMinutes(5), precision: TimeSpan.FromSeconds(5));
        auction.ExtensionCount.Should().Be(1);
        extension!.PreviousEnd.Should().BeCloseTo(originalEndsAt, precision: TimeSpan.FromSeconds(2));
        extension.NewEnd.Should().BeCloseTo(originalEndsAt.AddMinutes(5), precision: TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task BidOutsideAntiSnipeWindow_DoesNotExtend()
    {
        var endsAt = DateTime.UtcNow.AddMinutes(10);
        await using var setupScope = _factory.Services.CreateAsyncScope();
        var (originalEndsAt, startingBidCount) = await _factory.ResetTestAuctionAsync(
            setupScope.ServiceProvider.GetRequiredService<AuctionDbContext>(), endsAt);

        // Bid 10 min before close — outside 5-min window
        await PublishBidAsync(originalEndsAt.AddMinutes(-10), 200m);

        // Wait for bid counter to advance (confirms the handler completed)
        await PollUntilAsync(async () =>
        {
            await using var s = _factory.Services.CreateAsyncScope();
            var db = s.ServiceProvider.GetRequiredService<AuctionDbContext>();
            var a = await db.Auctions.AsNoTracking().FirstAsync(x => x.Id == AuctionApiFactory.TestAuctionId);
            return a.BidCount > startingBidCount ? a : null;
        });

        await using var verifyScope = _factory.Services.CreateAsyncScope();
        var dbFinal = verifyScope.ServiceProvider.GetRequiredService<AuctionDbContext>();
        var auction = await dbFinal.Auctions.AsNoTracking().FirstAsync(a => a.Id == AuctionApiFactory.TestAuctionId);
        var extCount = await dbFinal.AuctionExtensions
            .CountAsync(x => x.AuctionId == AuctionApiFactory.TestAuctionId);

        auction.EndsAt.Should().BeCloseTo(originalEndsAt, precision: TimeSpan.FromSeconds(2),
            because: "a bid outside the anti-snipe window must not extend EndsAt");
        extCount.Should().Be(0);
    }

    // ---------------------------------------------------------------------------
    // ADR-040: extension cap enforced
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task MaxExtensionsCap_11thBid_DoesNotExtend()
    {
        var endsAt = DateTime.UtcNow.AddMinutes(10);
        await using var setupScope = _factory.Services.CreateAsyncScope();
        var (originalEndsAt, startingBidCount) = await _factory.ResetTestAuctionAsync(
            setupScope.ServiceProvider.GetRequiredService<AuctionDbContext>(), endsAt);

        // Publish 11 bids, all within the anti-snipe window
        var publishTasks = Enumerable.Range(1, 11)
            .Select(i => PublishBidAsync(originalEndsAt.AddMinutes(-3), 100m + i));
        await Task.WhenAll(publishTasks);

        // Wait until all 11 bids have been processed
        await PollUntilAsync(async () =>
        {
            await using var s = _factory.Services.CreateAsyncScope();
            var db = s.ServiceProvider.GetRequiredService<AuctionDbContext>();
            var a = await db.Auctions.AsNoTracking().FirstAsync(x => x.Id == AuctionApiFactory.TestAuctionId);
            return a.BidCount >= startingBidCount + 11 ? a : null;
        }, attempts: 40, delayMs: 500);

        await using var verifyScope = _factory.Services.CreateAsyncScope();
        var dbFinal = verifyScope.ServiceProvider.GetRequiredService<AuctionDbContext>();
        var auction = await dbFinal.Auctions.AsNoTracking().FirstAsync(a => a.Id == AuctionApiFactory.TestAuctionId);
        var totalExtensions = await dbFinal.AuctionExtensions
            .CountAsync(x => x.AuctionId == AuctionApiFactory.TestAuctionId);

        auction.ExtensionCount.Should().Be(10, "cap is 10 extensions maximum (ADR-040)");
        totalExtensions.Should().Be(10, "auction_extensions must have exactly 10 rows");
    }

    // ---------------------------------------------------------------------------
    // Idempotency: duplicate MessageId must not produce a second extension
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task DuplicateEvent_IsIdempotent()
    {
        var endsAt = DateTime.UtcNow.AddMinutes(10);
        await using var setupScope = _factory.Services.CreateAsyncScope();
        await _factory.ResetTestAuctionAsync(
            setupScope.ServiceProvider.GetRequiredService<AuctionDbContext>(), endsAt);

        var messageId = Guid.NewGuid().ToString("N");
        var bidId = Guid.NewGuid();
        var createdAt = endsAt.AddMinutes(-3);

        // Publish the same event twice with the identical MessageId
        for (var i = 0; i < 2; i++)
        {
            await using var scope = _factory.Services.CreateAsyncScope();
            var publisher = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();
            await publisher.Publish(new BidPlacedEvent(messageId, bidId,
                AuctionApiFactory.TestAuctionId, "test-bidder", 200m, createdAt));

            // Give the first message time to be fully processed before the replay
            if (i == 0) await Task.Delay(2000);
        }

        await Task.Delay(2000); // allow second message to be processed (and skipped)

        await using var verifyScope = _factory.Services.CreateAsyncScope();
        var db = verifyScope.ServiceProvider.GetRequiredService<AuctionDbContext>();
        var extCount = await db.AuctionExtensions
            .CountAsync(x => x.AuctionId == AuctionApiFactory.TestAuctionId);
        var auction = await db.Auctions.AsNoTracking().FirstAsync(a => a.Id == AuctionApiFactory.TestAuctionId);

        extCount.Should().Be(1, "duplicate MessageId must be blocked by ProcessedMessages guard");
        auction.BidCount.Should().Be(1, "bid count must reflect exactly one processed event");
    }

    // ---------------------------------------------------------------------------
    // ADR-041: AuctionStatusChangedEvent published after extension
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task BidInWindow_PublishesAuctionStatusChangedEvent_WithUpdatedEndsAt()
    {
        var endsAt = DateTime.UtcNow.AddMinutes(10);
        await using var setupScope = _factory.Services.CreateAsyncScope();
        var (originalEndsAt, _) = await _factory.ResetTestAuctionAsync(
            setupScope.ServiceProvider.GetRequiredService<AuctionDbContext>(), endsAt);

        // Bind a temp exclusive queue to auctions.fanout before publishing the bid
        var connFactory = new ConnectionFactory
        {
            HostName = _factory.RabbitHost,
            Port = _factory.RabbitPort,
            UserName = _factory.RabbitUser,
            Password = _factory.RabbitPass,
            VirtualHost = _factory.RabbitVHost,
        };

        await using var conn = await connFactory.CreateConnectionAsync();
        await using var ch = await conn.CreateChannelAsync();
        var tempQueue = $"test.extension-event.{Guid.NewGuid():N}";
        await ch.QueueDeclareAsync(tempQueue, durable: false, exclusive: true, autoDelete: true);
        // Exchange is declared by AuctionService's MassTransit setup on startup
        await ch.QueueBindAsync(tempQueue, exchange: "auctions.fanout", routingKey: "");

        // Place a bid within the anti-snipe window
        await PublishBidAsync(originalEndsAt.AddMinutes(-3), 250m);

        // Poll the temp queue for AuctionStatusChangedEvent
        System.Text.Json.JsonDocument? eventDoc = null;
        for (var i = 0; i < 20 && eventDoc is null; i++)
        {
            await Task.Delay(500);
            var result = await ch.BasicGetAsync(tempQueue, autoAck: true);
            if (result is null) continue;

            var doc = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonDocument>(
                result.Body.ToArray());
            var typeArray = doc?.RootElement.GetProperty("messageType");
            if (typeArray?.EnumerateArray().Any(t =>
                    t.GetString()?.Contains("AuctionStatusChangedEvent") == true) == true)
                eventDoc = doc;
        }

        eventDoc.Should().NotBeNull(
            "BidPlacedHandler must publish AuctionStatusChangedEvent when extending EndsAt (ADR-041)");

        var publishedEndsAt = eventDoc!.RootElement
            .GetProperty("message").GetProperty("endsAt").GetDateTime();
        publishedEndsAt.Should().BeCloseTo(originalEndsAt.AddMinutes(5), precision: TimeSpan.FromSeconds(5),
            because: "the published EndsAt must match the extended value");
    }

    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    private async Task PublishBidAsync(DateTime createdAtUtc, decimal amount)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var publisher = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();
        await publisher.Publish(new BidPlacedEvent(
            MessageId: Guid.NewGuid().ToString("N"),
            BidId: Guid.NewGuid(),
            AuctionId: AuctionApiFactory.TestAuctionId,
            BidderId: "test-bidder",
            Amount: amount,
            CreatedAtUtc: createdAtUtc));
    }

    private async Task<AuctionExtension?> PollForExtensionAsync(int expectedCount,
        int attempts = 20, int delayMs = 500)
    {
        for (var i = 0; i < attempts; i++)
        {
            await Task.Delay(delayMs);
            await using var scope = _factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AuctionDbContext>();
            var count = await db.AuctionExtensions
                .CountAsync(x => x.AuctionId == AuctionApiFactory.TestAuctionId);
            if (count >= expectedCount)
                return await db.AuctionExtensions.AsNoTracking()
                    .FirstOrDefaultAsync(x => x.AuctionId == AuctionApiFactory.TestAuctionId);
        }
        return null;
    }

    private static async Task<T?> PollUntilAsync<T>(Func<Task<T?>> check,
        int attempts = 20, int delayMs = 500) where T : class
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
