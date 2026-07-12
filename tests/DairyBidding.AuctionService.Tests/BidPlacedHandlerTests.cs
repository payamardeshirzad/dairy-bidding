using DairyBidding.AuctionService;
using DairyBidding.AuctionService.Data;
using DairyBidding.AuctionService.Messaging.Handlers;
using DairyBidding.Contracts.Events;
using FluentAssertions;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

public class BidPlacedHandlerTests : IAsyncDisposable
{
    private readonly AuctionDbContext _db;
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly BidPlacedHandler _handler;

    private const string TestAuctionId = "AUC-UNIT-001";

    private static readonly AntiSnipeOptions DefaultOptions = new()
    {
        WindowMinutes = 5,
        ExtensionMinutes = 5,
        MaxExtensionsPerAuction = 10,
    };

    public BidPlacedHandlerTests()
    {
        // Each test gets its own isolated in-memory database
        var dbOptions = new DbContextOptionsBuilder<AuctionDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AuctionDbContext(dbOptions);

        _publishEndpoint = Substitute.For<IPublishEndpoint>();

        _handler = new BidPlacedHandler(
            _db,
            _publishEndpoint,
            Options.Create(DefaultOptions),
            Substitute.For<ILogger<BidPlacedHandler>>());
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private async Task<Auction> SeedAuctionAsync(DateTime endsAt, int extensionCount = 0,
        decimal currentPrice = 100m)
    {
        var auction = new Auction
        {
            Id = TestAuctionId,
            Title = "Unit Test Auction",
            Description = string.Empty,
            StartingPrice = 100m,
            CurrentPrice = currentPrice,
            StartsAt = DateTime.UtcNow.AddHours(-1),
            EndsAt = endsAt,
            Status = AuctionStatus.Active,
            CreatedAtUtc = DateTime.UtcNow,
            ExtensionCount = extensionCount,
        };
        _db.Auctions.Add(auction);
        await _db.SaveChangesAsync();
        return auction;
    }

    private static BidPlacedEvent MakeBid(DateTime createdAtUtc, decimal amount = 200m,
        string? messageId = null) =>
        new(
            MessageId: messageId ?? Guid.NewGuid().ToString("N"),
            BidId: Guid.NewGuid(),
            AuctionId: TestAuctionId,
            BidderId: "bidder-1",
            Amount: amount,
            CreatedAtUtc: createdAtUtc);

    // -------------------------------------------------------------------------
    // ADR-034: Core anti-snipe window logic
    // -------------------------------------------------------------------------

    [Fact]
    public async Task BidInAntiSnipeWindow_ExtendsEndsAt_RecordsExtensionRow()
    {
        var endsAt = DateTime.UtcNow.AddMinutes(10);
        await SeedAuctionAsync(endsAt);

        var evt = MakeBid(endsAt.AddMinutes(-3)); // 3 min before close: inside 5-min window
        await _handler.HandleAsync(evt);

        var auction = await _db.Auctions.FirstAsync(a => a.Id == TestAuctionId);
        var extension = await _db.AuctionExtensions.SingleAsync();

        auction.EndsAt.Should().BeCloseTo(endsAt.AddMinutes(5), TimeSpan.FromSeconds(1));
        auction.ExtensionCount.Should().Be(1);
        auction.BidCount.Should().Be(1);

        extension.AuctionId.Should().Be(TestAuctionId);
        extension.BidId.Should().Be(evt.BidId);
        extension.PreviousEnd.Should().BeCloseTo(endsAt, TimeSpan.FromSeconds(1));
        extension.NewEnd.Should().BeCloseTo(endsAt.AddMinutes(5), TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task BidOutsideAntiSnipeWindow_DoesNotExtend()
    {
        var endsAt = DateTime.UtcNow.AddMinutes(10);
        await SeedAuctionAsync(endsAt);

        var evt = MakeBid(endsAt.AddMinutes(-10)); // 10 min before close: outside 5-min window
        await _handler.HandleAsync(evt);

        var auction = await _db.Auctions.FirstAsync(a => a.Id == TestAuctionId);
        auction.EndsAt.Should().BeCloseTo(endsAt, TimeSpan.FromSeconds(1));
        auction.ExtensionCount.Should().Be(0);
        auction.BidCount.Should().Be(1);
        (await _db.AuctionExtensions.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task BidAtExactWindowBoundary_Extends()
    {
        // timeToClose == WindowMinutes exactly — boundary is inclusive
        var endsAt = DateTime.UtcNow.AddMinutes(10);
        await SeedAuctionAsync(endsAt);

        var evt = MakeBid(endsAt.AddMinutes(-DefaultOptions.WindowMinutes));
        await _handler.HandleAsync(evt);

        var auction = await _db.Auctions.FirstAsync(a => a.Id == TestAuctionId);
        auction.ExtensionCount.Should().Be(1, "bid at exact window boundary must trigger extension");
    }

    [Fact]
    public async Task BidOneSecondPastWindowBoundary_DoesNotExtend()
    {
        // timeToClose == WindowMinutes + 1 second — just outside
        var endsAt = DateTime.UtcNow.AddMinutes(10);
        await SeedAuctionAsync(endsAt);

        var evt = MakeBid(endsAt.AddMinutes(-DefaultOptions.WindowMinutes).AddSeconds(-1));
        await _handler.HandleAsync(evt);

        var auction = await _db.Auctions.FirstAsync(a => a.Id == TestAuctionId);
        auction.ExtensionCount.Should().Be(0, "bid 1 second past the boundary must not extend");
    }

    [Fact]
    public async Task BidAfterAuctionClose_DoesNotExtend()
    {
        // timeToClose <= 0 — bid arrived after the auction closed
        var endsAt = DateTime.UtcNow.AddMinutes(10);
        await SeedAuctionAsync(endsAt);

        var evt = MakeBid(endsAt.AddMinutes(1)); // 1 min after close
        await _handler.HandleAsync(evt);

        var auction = await _db.Auctions.FirstAsync(a => a.Id == TestAuctionId);
        auction.ExtensionCount.Should().Be(0, "bid after close must not extend");
    }

    // -------------------------------------------------------------------------
    // ADR-040: Extension cap enforcement
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ExtensionCountAtCap_BidInWindow_DoesNotExtend()
    {
        var endsAt = DateTime.UtcNow.AddMinutes(10);
        await SeedAuctionAsync(endsAt, extensionCount: DefaultOptions.MaxExtensionsPerAuction);

        var evt = MakeBid(endsAt.AddMinutes(-3));
        await _handler.HandleAsync(evt);

        var auction = await _db.Auctions.FirstAsync(a => a.Id == TestAuctionId);
        auction.EndsAt.Should().BeCloseTo(endsAt, TimeSpan.FromSeconds(1),
            because: "cap is reached — EndsAt must not change");
        auction.ExtensionCount.Should().Be(DefaultOptions.MaxExtensionsPerAuction);
        (await _db.AuctionExtensions.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task ExtensionCountOneBelowCap_BidInWindow_Extends()
    {
        var endsAt = DateTime.UtcNow.AddMinutes(10);
        await SeedAuctionAsync(endsAt, extensionCount: DefaultOptions.MaxExtensionsPerAuction - 1);

        var evt = MakeBid(endsAt.AddMinutes(-3));
        await _handler.HandleAsync(evt);

        var auction = await _db.Auctions.FirstAsync(a => a.Id == TestAuctionId);
        auction.ExtensionCount.Should().Be(DefaultOptions.MaxExtensionsPerAuction,
            "one slot remains — bid must fill it");
        (await _db.AuctionExtensions.SingleAsync()).Should().NotBeNull();
    }

    // -------------------------------------------------------------------------
    // Idempotency
    // -------------------------------------------------------------------------

    [Fact]
    public async Task DuplicateMessageId_ReturnsEarly_NoChangesApplied()
    {
        var endsAt = DateTime.UtcNow.AddMinutes(10);
        await SeedAuctionAsync(endsAt);

        const string messageId = "dup-msg-id-001";
        _db.ProcessedMessages.Add(new ProcessedMessage
        {
            MessageId = messageId,
            ProcessedAtUtc = DateTime.UtcNow,
        });
        await _db.SaveChangesAsync();

        var evt = MakeBid(endsAt.AddMinutes(-3), messageId: messageId);
        await _handler.HandleAsync(evt);

        var auction = await _db.Auctions.FirstAsync(a => a.Id == TestAuctionId);
        auction.BidCount.Should().Be(0, "duplicate must be skipped entirely");
        auction.ExtensionCount.Should().Be(0);
        (await _db.AuctionExtensions.AnyAsync()).Should().BeFalse();
    }

    // -------------------------------------------------------------------------
    // Error path
    // -------------------------------------------------------------------------

    [Fact]
    public async Task AuctionNotFound_ThrowsInvalidOperationException()
    {
        var evt = MakeBid(DateTime.UtcNow); // no auction seeded
        var act = async () => await _handler.HandleAsync(evt);
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*not found*");
    }

    [Fact]
    public async Task EmptyMessageId_ThrowsInvalidOperationException()
    {
        var evt = MakeBid(DateTime.UtcNow, messageId: "");
        var act = async () => await _handler.HandleAsync(evt);
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*MessageId is required*");
    }

    // -------------------------------------------------------------------------
    // ADR-041: AuctionStatusChangedEvent propagation
    // -------------------------------------------------------------------------

    [Fact]
    public async Task BidInWindow_PublishesAuctionStatusChangedEvent_WithUpdatedEndsAt()
    {
        var endsAt = DateTime.UtcNow.AddMinutes(10);
        await SeedAuctionAsync(endsAt);

        var evt = MakeBid(endsAt.AddMinutes(-3));
        await _handler.HandleAsync(evt);

        await _publishEndpoint.Received(1).Publish(
            Arg.Is<AuctionStatusChangedEvent>(e =>
                e.AuctionId == TestAuctionId &&
                e.EndsAt > endsAt),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task BidOutsideWindow_DoesNotPublishAuctionStatusChangedEvent()
    {
        var endsAt = DateTime.UtcNow.AddMinutes(10);
        await SeedAuctionAsync(endsAt);

        var evt = MakeBid(endsAt.AddMinutes(-10));
        await _handler.HandleAsync(evt);

        await _publishEndpoint.DidNotReceive()
            .Publish(Arg.Any<AuctionStatusChangedEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExtensionAtCap_DoesNotPublishAuctionStatusChangedEvent()
    {
        var endsAt = DateTime.UtcNow.AddMinutes(10);
        await SeedAuctionAsync(endsAt, extensionCount: DefaultOptions.MaxExtensionsPerAuction);

        var evt = MakeBid(endsAt.AddMinutes(-3));
        await _handler.HandleAsync(evt);

        await _publishEndpoint.DidNotReceive()
            .Publish(Arg.Any<AuctionStatusChangedEvent>(), Arg.Any<CancellationToken>());
    }

    // -------------------------------------------------------------------------
    // Bid counter updates (always applied regardless of extension)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task BidAboveCurrentPrice_UpdatesCurrentPrice()
    {
        var endsAt = DateTime.UtcNow.AddMinutes(10);
        await SeedAuctionAsync(endsAt, currentPrice: 100m);

        var evt = MakeBid(endsAt.AddMinutes(-10), amount: 350m);
        await _handler.HandleAsync(evt);

        var auction = await _db.Auctions.FirstAsync(a => a.Id == TestAuctionId);
        auction.CurrentPrice.Should().Be(350m);
        auction.BidCount.Should().Be(1);
    }

    [Fact]
    public async Task BidBelowCurrentPrice_DoesNotUpdateCurrentPrice()
    {
        var endsAt = DateTime.UtcNow.AddMinutes(10);
        await SeedAuctionAsync(endsAt, currentPrice: 500m);

        var evt = MakeBid(endsAt.AddMinutes(-10), amount: 200m); // below 500
        await _handler.HandleAsync(evt);

        var auction = await _db.Auctions.FirstAsync(a => a.Id == TestAuctionId);
        auction.CurrentPrice.Should().Be(500m, "lower bid must not overwrite the current price");
        auction.BidCount.Should().Be(1, "bid count still increments regardless of price");
    }

    public async ValueTask DisposeAsync() => await _db.DisposeAsync();
}
