using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Networks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;

public sealed class BiddingApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly INetwork _network = new NetworkBuilder().Build();
    private readonly PostgreSqlContainer _postgres;
    private readonly RabbitMqContainer _rabbit;

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
                ["RabbitMQ:User"] = "guest",
                ["RabbitMQ:Pass"] = "guest",
                ["RabbitMQ:VHost"] = "/",
                ["Jwt:Issuer"] = "dairy-identity",
                ["Jwt:Audience"] = "dairy-bidding-api",
                ["Jwt:SigningKey"] = "THIS_IS_DEV_ONLY_CHANGE_ME_1234567890"
            };
            config.AddInMemoryCollection(dict);
        });
    }

    public async Task InitializeAsync()
    {
        await _network.CreateAsync();
        await _postgres.StartAsync();
        await _rabbit.StartAsync();
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await _rabbit.DisposeAsync();
        await _postgres.DisposeAsync();
        await _network.DisposeAsync();
    }
}