using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace DairyBidding.BiddingService.Messaging;

public interface IBidPlacedPublisher
{
    Task PublishAsync(BidPlacedEvent evt, string? correlationId = null, CancellationToken cancellationToken = default);
    ValueTask DisposeAsync();
}

public class BidPlacedPublisher : IBidPlacedPublisher, IHostedService, IAsyncDisposable
{
    private IConnection? _connection;
    private IChannel? _channel;
    private readonly RabbitMqOptions _options;
    private readonly ILogger<BidPlacedPublisher> _logger;
    private const string Exchange = "bidding.events";
    private const string Queue = "bidding.bidplaced";
    private const string RoutingKey = "bid.placed";
    private volatile bool _ready;

    public BidPlacedPublisher(IOptions<RabbitMqOptions> options, ILogger<BidPlacedPublisher> logger)
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
            RequestedConnectionTimeout = TimeSpan.FromSeconds(10),
            SocketReadTimeout = TimeSpan.FromSeconds(10),
            SocketWriteTimeout = TimeSpan.FromSeconds(10),
            AutomaticRecoveryEnabled = true
        };

        _logger.LogInformation("Connecting to RabbitMQ at {Host}:{Port} vhost={VHost}", _options.Host, _options.Port, _options.VHost);

        _connection = await factory.CreateConnectionAsync(cancellationToken);
        _channel = await _connection.CreateChannelAsync(cancellationToken: cancellationToken);

        await _channel.ExchangeDeclareAsync(exchange: Exchange, type: ExchangeType.Topic, durable: true, autoDelete: false, cancellationToken: cancellationToken);
        await _channel.QueueDeclareAsync(queue: Queue, durable: true, exclusive: false, autoDelete: false, cancellationToken: cancellationToken);
        await _channel.QueueBindAsync(queue: Queue, exchange: Exchange, routingKey: RoutingKey, cancellationToken: cancellationToken);

        _ready = true;
        _logger.LogInformation("RabbitMQ publisher is ready.");
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _ready = false;
        return Task.CompletedTask;
    }

    public async Task PublishAsync(BidPlacedEvent evt, string? correlationId = null, CancellationToken cancellationToken = default)
    {
                if (!_ready || _channel is null) throw new InvalidOperationException("Publisher not initialized.");
        var json = JsonSerializer.Serialize(evt);
        var body = Encoding.UTF8.GetBytes(json);

        var props = new BasicProperties
        {
            Persistent = true,
            ContentType = "application/json",
            MessageId = evt.MessageId,
            Headers = new Dictionary<string, object?>()
        };

        if (!string.IsNullOrWhiteSpace(correlationId))
            props.Headers["x-correlation-id"] = correlationId;

        await _channel.BasicPublishAsync(
            exchange: Exchange,
            routingKey: RoutingKey,
            mandatory: false,
            basicProperties: props,
            body: body,
            cancellationToken: cancellationToken);
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
            _logger.LogWarning(ex, "Error disposing RabbitMQ publisher resources.");
        }
    }
}