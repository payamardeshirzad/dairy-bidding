using System.Text;
using DairyBidding.AuctionService.Data;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Networks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;

public sealed class AuctionApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly INetwork _network = new NetworkBuilder().Build();
    private readonly PostgreSqlContainer _postgres;
    private readonly RabbitMqContainer _rabbit;

    public const string TestAuctionId = "AUC-ANTI-SNIPE-INT-001";

    public AuctionApiFactory()
    {
        _postgres = new PostgreSqlBuilder("postgres:16")
            .WithDatabase("dairy_auction_test")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .WithNetwork(_network)
            .Build();

        _rabbit = new RabbitMqBuilder("rabbitmq:3.13-management")
            .WithUsername("guest")
            .WithPassword("guest")
            .WithNetwork(_network)
            .Build();
    }

    public string RabbitHost => _rabbit.Hostname;
    public int RabbitPort => _rabbit.GetMappedPublicPort(5672);
    public string RabbitUser => "guest";
    public string RabbitPass => "guest";
    public string RabbitVHost => "/";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Test");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            var dict = new Dictionary<string, string?>
            {
                ["ConnectionStrings:Postgres"] = _postgres.GetConnectionString(),
                ["RabbitMQ:Host"] = _rabbit.Hostname,
                ["RabbitMQ:Port"] = _rabbit.GetMappedPublicPort(5672).ToString(),
                ["RabbitMQ:Username"] = "guest",
                ["RabbitMQ:Password"] = "guest",
                ["RabbitMQ:VirtualHost"] = "/",
                ["Jwt:Audience"] = "dairy-bidding-api",
                ["AntiSnipe:WindowMinutes"] = "5",
                ["AntiSnipe:ExtensionMinutes"] = "5",
                ["AntiSnipe:MaxExtensionsPerAuction"] = "10",
            };
            config.AddInMemoryCollection(dict);
        });

        builder.ConfigureServices(services =>
        {
            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, opts =>
            {
                opts.Authority = null;
                opts.Configuration = null;
                opts.ConfigurationManager = null;
                opts.TokenValidationParameters.ValidateIssuerSigningKey = true;
                opts.TokenValidationParameters.IssuerSigningKey =
                    new SymmetricSecurityKey(Encoding.UTF8.GetBytes("THIS_IS_DEV_ONLY_CHANGE_ME_1234567890"));
                opts.TokenValidationParameters.ValidIssuer = "dairy-identity";
                opts.TokenValidationParameters.ValidateIssuer = true;
                opts.TokenValidationParameters.ValidAudience = "dairy-bidding-api";
                opts.TokenValidationParameters.ValidateAudience = true;
            });
        });
    }

    public async Task InitializeAsync()
    {
        await _network.CreateAsync();
        await _postgres.StartAsync();
        await _rabbit.StartAsync();

        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AuctionDbContext>();
        await db.Database.MigrateAsync();

        await ResetTestAuctionAsync(db, DateTime.UtcNow.AddMinutes(10));
    }

    /// <summary>Resets the shared test auction to a clean state and returns the fresh EndsAt.</summary>
    public async Task<(DateTime EndsAt, int BidCount)> ResetTestAuctionAsync(
        AuctionDbContext db, DateTime endsAt)
    {
        // Remove any existing extensions for this auction
        var extensions = await db.AuctionExtensions
            .Where(x => x.AuctionId == TestAuctionId)
            .ToListAsync();
        db.AuctionExtensions.RemoveRange(extensions);

        var auction = await db.Auctions.FirstOrDefaultAsync(a => a.Id == TestAuctionId);
        if (auction is null)
        {
            auction = new Auction
            {
                Id = TestAuctionId,
                Title = "Anti-Snipe Integration Test Auction",
                Description = "Shared test auction for anti-snipe tests.",
                StartingPrice = 100m,
                StartsAt = DateTime.UtcNow.AddHours(-1),
                EndsAt = endsAt,
                Status = AuctionStatus.Active,
                CreatedAtUtc = DateTime.UtcNow,
            };
            db.Auctions.Add(auction);
        }
        else
        {
            auction.EndsAt = endsAt;
            auction.ExtensionCount = 0;
            auction.BidCount = 0;
            auction.CurrentPrice = auction.StartingPrice;
            auction.RowVersion++;
        }

        await db.SaveChangesAsync();
        return (endsAt, 0);
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await _rabbit.DisposeAsync();
        await _postgres.DisposeAsync();
        await _network.DisposeAsync();
    }
}
