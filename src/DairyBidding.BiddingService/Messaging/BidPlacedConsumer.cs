using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace DairyBidding.BiddingService.Messaging;

public class BidPlacedConsumer : BackgroundService
{
    private readonly ILogger<BidPlacedConsumer> _logger;
    private readonly RabbitMqOptions _options;

    private IConnection? _connection;
    private IModel? _channel;

    private const string Exchange = "bidding.events";
    private const string Queue = "bidding.bidplaced";
    private const string RoutingKey = "bid.placed";

    public BidPlacedConsumer(
        IOptions<RabbitMqOptions> options,
        ILogger<BidPlacedConsumer> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public override Task StartAsync(CancellationToken cancellationToken)
    {
        var factory = new ConnectionFactory
        {
            HostName = _options.Host,
            Port = _options.Port,
            UserName = _options.User,
            Password = _options.Pass,
            VirtualHost = _options.VHost,
            DispatchConsumersAsync = false,
            AutomaticRecoveryEnabled = true
        };

        _connection = factory.CreateConnection();
        _channel = _connection.CreateModel();

        _channel.ExchangeDeclare(exchange: Exchange, type: ExchangeType.Topic, durable: true, autoDelete: false);
        _channel.QueueDeclare(queue: Queue, durable: true, exclusive: false, autoDelete: false);
        _channel.QueueBind(queue: Queue, exchange: Exchange, routingKey: RoutingKey);

        // Process one message at a time for predictable behavior in this step
        _channel.BasicQos(prefetchSize: 0, prefetchCount: 1, global: false);

        _logger.LogInformation("BidPlacedConsumer connected to RabbitMQ {Host}:{Port}, vhost {VHost}",
            _options.Host, _options.Port, _options.VHost);

        return base.StartAsync(cancellationToken);
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_channel is null)
            throw new InvalidOperationException("RabbitMQ channel is not initialized.");

        var consumer = new EventingBasicConsumer(_channel);

        consumer.Received += (_, ea) =>
        {
            try
            {
                var json = Encoding.UTF8.GetString(ea.Body.ToArray());
                var evt = JsonSerializer.Deserialize<BidPlacedEvent>(json);

                if (evt is null)
                    throw new InvalidOperationException("Deserialized BidPlacedEvent is null.");

                _logger.LogInformation(
                    "Consumed BidPlaced: BidId={BidId}, AuctionId={AuctionId}, BidderId={BidderId}, Amount={Amount}, CreatedAtUtc={CreatedAtUtc}",
                    evt.BidId, evt.AuctionId, evt.BidderId, evt.Amount, evt.CreatedAtUtc);

                _channel.BasicAck(deliveryTag: ea.DeliveryTag, multiple: false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process BidPlaced message. Nack without requeue.");
                _channel.BasicNack(deliveryTag: ea.DeliveryTag, multiple: false, requeue: false);
            }
        };

        _channel.BasicConsume(
            queue: Queue,
            autoAck: false,
            consumer: consumer);

        _logger.LogInformation("BidPlacedConsumer started. Waiting for messages on queue '{Queue}'", Queue);

        return Task.CompletedTask;
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        try
        {
            _channel?.Close();
            _connection?.Close();
        }
        catch
        {
            // ignore shutdown exceptions in dev step
        }

        return base.StopAsync(cancellationToken);
    }

    public override void Dispose()
    {
        _channel?.Dispose();
        _connection?.Dispose();
        base.Dispose();
    }
}