using System.Text;
using System.Text.Json;
using DairyBidding.Contracts.Events;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace DairyBidding.AuctionService.Messaging;

public interface IAuctionEventPublisher
{
    Task PublishStatusChangedAsync(AuctionStatusChangedEvent evt, CancellationToken cancellationToken = default);
}

public class AuctionEventPublisher : IAuctionEventPublisher, IHostedService, IAsyncDisposable
{
    private IConnection? _connection;
    private IChannel? _channel;
    private readonly RabbitMqOptions _options;
    private readonly ILogger<AuctionEventPublisher> _logger;
    private volatile bool _ready;

    private const string Exchange = "auction.events";
    private const string Queue = "auction.status-changed";
    private const string RoutingKey = "auction.status.changed";

    public AuctionEventPublisher(IOptions<RabbitMqOptions> options, ILogger<AuctionEventPublisher> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var factory = new ConnectionFactory
        {
            HostName = _options.Host,
            Port = _options.Port,
            UserName = _options.User,
            Password = _options.Pass,
            VirtualHost = _options.VHost,
            AutomaticRecoveryEnabled = true,
            RequestedConnectionTimeout = TimeSpan.FromSeconds(10)
        };

        _logger.LogInformation("Connecting to RabbitMQ at {Host}:{Port} vhost={VHost}", _options.Host, _options.Port, _options.VHost);

        _connection = await factory.CreateConnectionAsync(cancellationToken);
        _channel = await _connection.CreateChannelAsync(cancellationToken: cancellationToken);

        await _channel.ExchangeDeclareAsync(Exchange, ExchangeType.Topic, durable: true, autoDelete: false, cancellationToken: cancellationToken);
        await _channel.QueueDeclareAsync(Queue, durable: true, exclusive: false, autoDelete: false, cancellationToken: cancellationToken);
        await _channel.QueueBindAsync(Queue, Exchange, RoutingKey, cancellationToken: cancellationToken);

        _ready = true;
        _logger.LogInformation("AuctionEventPublisher is ready.");
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _ready = false;
        return Task.CompletedTask;
    }

    public async Task PublishStatusChangedAsync(AuctionStatusChangedEvent evt, CancellationToken cancellationToken = default)
    {
        if (!_ready || _channel is null)
            throw new InvalidOperationException("Publisher not initialized.");

        var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(evt));
        var props = new BasicProperties
        {
            Persistent = true,
            ContentType = "application/json",
            MessageId = evt.MessageId
        };

        await _channel.BasicPublishAsync(
            exchange: Exchange,
            routingKey: RoutingKey,
            mandatory: false,
            basicProperties: props,
            body: body,
            cancellationToken: cancellationToken);

        _logger.LogInformation("Published AuctionStatusChanged: AuctionId={AuctionId}, Status={Status}", evt.AuctionId, evt.Status);
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_channel is not null) await _channel.DisposeAsync();
            if (_connection is not null) await _connection.DisposeAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error disposing AuctionEventPublisher resources.");
        }
    }
}
