using System.Text;
using DairyBidding.BiddingService.Data;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Networks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;

public sealed class BiddingApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly INetwork _network = new NetworkBuilder().Build();
    private readonly PostgreSqlContainer _postgres;
    private readonly RabbitMqContainer _rabbit;

    public const string TestAuctionId = "AUC-TEST-INTEGRATION-001";

    public BiddingApiFactory()
    {
        _postgres = new PostgreSqlBuilder("postgres:16")
            .WithDatabase("dairy_bidding")
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

    public string PostgresConnectionString => _postgres.GetConnectionString();
    public string RabbitHost => _rabbit.Hostname;
    public int RabbitPort => _rabbit.GetMappedPublicPort(5672);
    public string RabbitUser => "guest";
    public string RabbitPass => "guest";
    public string RabbitVHost => "/";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

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
            };
            config.AddInMemoryCollection(dict);
        });

        // Override JWT bearer to use symmetric key — no Duende IS running during tests
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

        // Run migrations then seed a known active auction into the read model
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BiddingDbContext>();
        await db.Database.EnsureCreatedAsync();

        if (!db.AuctionReadModels.Any(a => a.AuctionId == TestAuctionId))
        {
            db.AuctionReadModels.Add(new AuctionReadModel
            {
                AuctionId = TestAuctionId,
                Title = "Integration Test Auction",
                Status = "Active",
                StartingPrice = 100m,
                StartsAt = DateTime.UtcNow.AddHours(-1),
                EndsAt = DateTime.UtcNow.AddHours(23),
                UpdatedAtUtc = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await _rabbit.DisposeAsync();
        await _postgres.DisposeAsync();
        await _network.DisposeAsync();
    }
}