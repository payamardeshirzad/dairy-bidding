using System.Text;
using System.Text.Json;
using DairyBidding.BiddingService.Data;
using Microsoft.EntityFrameworkCore;
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
    private readonly IServiceScopeFactory _scopeFactory;

    private IConnection? _connection;
    private IModel? _channel;

    private const string Exchange = "bidding.events";
    private const string Queue = "bidding.bidplaced";
    private const string RoutingKey = "bid.placed";

    public BidPlacedConsumer(
        IOptions<RabbitMqOptions> options,
        ILogger<BidPlacedConsumer> logger,
        IServiceScopeFactory scopeFactory)
    {
        _options = options.Value;
        _logger = logger;
        _scopeFactory = scopeFactory;
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

        consumer.Received += async (_, ea) =>
        {
            try
            {
                var json = Encoding.UTF8.GetString(ea.Body.ToArray());
                var evt = JsonSerializer.Deserialize<BidPlacedEvent>(json)
                          ?? throw new InvalidOperationException("Invalid message payload.");
                if (string.IsNullOrWhiteSpace(evt.MessageId))
                    throw new InvalidOperationException("MessageId is required for idempotency.");

                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<BiddingDbContext>();

                var alreadyProcessed = await db.ProcessedMessages
                    .AnyAsync(x => x.MessageId == evt.MessageId);

                if (alreadyProcessed)
                {
                    _logger.LogInformation("Duplicate message skipped. MessageId={MessageId}", evt.MessageId);
                    _channel.BasicAck(ea.DeliveryTag, false);
                    return;
                }

                // TODO: your actual side-effect here (read model update, etc.)
                _logger.LogInformation("Processing message MessageId={MessageId}, BidId={BidId}", evt.MessageId, evt.BidId);

                db.ProcessedMessages.Add(new ProcessedMessage
                {
                    MessageId = evt.MessageId,
                    ProcessedAtUtc = DateTime.UtcNow
                });

                await db.SaveChangesAsync();
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