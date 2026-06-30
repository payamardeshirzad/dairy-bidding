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

    private const string MainExchange = "bidding.events";
    private const string MainQueue = "bidding.bidplaced";
    private const string MainRoutingKey = "bid.placed";

    private const string RetryExchange = "bidding.events.retry";
    private const string RetryQueue = "bidding.bidplaced.retry";
    private const string RetryRoutingKey = "bid.placed.retry";

    private const string DlxExchange = "bidding.events.dlx";
    private const string DlqQueue = "bidding.bidplaced.dlq";
    private const string DlqRoutingKey = "bid.placed.dlq";

    private const int RetryDelayMs = 5000;   // 5s between attempts
    private const int MaxRetries = 3;        // after 3 retries -> DLQ

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

        // Exchanges
        _channel.ExchangeDeclare(MainExchange, ExchangeType.Topic, durable: true, autoDelete: false);
        _channel.ExchangeDeclare(RetryExchange, ExchangeType.Topic, durable: true, autoDelete: false);
        _channel.ExchangeDeclare(DlxExchange, ExchangeType.Topic, durable: true, autoDelete: false);

        // Main queue
        _channel.QueueDeclare(MainQueue, durable: true, exclusive: false, autoDelete: false);
        _channel.QueueBind(MainQueue, MainExchange, MainRoutingKey);

        // Retry queue: dead-letters back to main exchange after TTL
        var retryArgs = new Dictionary<string, object>
        {
            ["x-message-ttl"] = RetryDelayMs,
            ["x-dead-letter-exchange"] = MainExchange,
            ["x-dead-letter-routing-key"] = MainRoutingKey
        };
        _channel.QueueDeclare(RetryQueue, durable: true, exclusive: false, autoDelete: false, arguments: retryArgs);
        _channel.QueueBind(RetryQueue, RetryExchange, RetryRoutingKey);

        // DLQ
        _channel.QueueDeclare(DlqQueue, durable: true, exclusive: false, autoDelete: false);
        _channel.QueueBind(DlqQueue, DlxExchange, DlqRoutingKey);

        _channel.BasicQos(0, 1, false);
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

                var read = await db.AuctionBidReadModels
                    .FirstOrDefaultAsync(x => x.AuctionId == evt.AuctionId);

                if (read is null)
                {
                    read = new AuctionBidReadModel
                    {
                        AuctionId = evt.AuctionId,
                        HighestBidAmount = evt.Amount,
                        HighestBidderId = evt.BidderId,
                        TotalBids = 1,
                        UpdatedAtUtc = DateTime.UtcNow
                    };
                    db.AuctionBidReadModels.Add(read);
                }
                else
                {
                    read.TotalBids += 1;
                    if (evt.Amount > read.HighestBidAmount)
                    {
                        read.HighestBidAmount = evt.Amount;
                        read.HighestBidderId = evt.BidderId;
                    }
                    read.UpdatedAtUtc = DateTime.UtcNow;
                }

                // mark message processed (idempotency)
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
                var currentRetry = GetRetryCount(ea.BasicProperties);
                var nextRetry = currentRetry + 1;

                try
                {
                    if (nextRetry <= MaxRetries)
                    {
                        var retryProps = CreateRepublishProperties(ea.BasicProperties, nextRetry);

                        _channel!.BasicPublish(
                            exchange: RetryExchange,
                            routingKey: RetryRoutingKey,
                            basicProperties: retryProps,
                            body: ea.Body);

                        _logger.LogWarning(ex,
                            "Processing failed. Message requeued to RETRY. RetryAttempt={RetryAttempt}/{MaxRetries}, DeliveryTag={DeliveryTag}",
                            nextRetry, MaxRetries, ea.DeliveryTag);
                    }
                    else
                    {
                        var dlqProps = CreateRepublishProperties(ea.BasicProperties, currentRetry);

                        _channel!.BasicPublish(
                            exchange: DlxExchange,
                            routingKey: DlqRoutingKey,
                            basicProperties: dlqProps,
                            body: ea.Body);

                        _logger.LogError(ex,
                            "Processing failed. Message sent to DLQ after max retries. Retries={Retries}, DeliveryTag={DeliveryTag}",
                            currentRetry, ea.DeliveryTag);
                    }

                    // Ack original message because we safely republished it
                    _channel!.BasicAck(ea.DeliveryTag, false);
                }
                catch (Exception republishEx)
                {
                    _logger.LogError(republishEx,
                        "Failed to republish message to retry/DLQ. Nack with requeue=true to avoid loss. DeliveryTag={DeliveryTag}",
                        ea.DeliveryTag);

                    _channel!.BasicNack(ea.DeliveryTag, false, requeue: true);
                }
            }
        };

        _channel.BasicConsume(
            queue: MainQueue,
            autoAck: false,
            consumer: consumer);

        _logger.LogInformation("BidPlacedConsumer started. Waiting for messages on queue '{Queue}'", MainQueue);

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
    private static int GetRetryCount(IBasicProperties props)
    {
        if (props.Headers is null) return 0;
        if (!props.Headers.TryGetValue("x-retry-count", out var raw) || raw is null) return 0;

        return raw switch
        {
            byte b => b,
            sbyte sb => sb,
            short s => s,
            ushort us => us,
            int i => i,
            long l => (int)l,
            byte[] bytes when int.TryParse(Encoding.UTF8.GetString(bytes), out var parsed) => parsed,
            _ => 0
        };
    }

    private IBasicProperties CreateRepublishProperties(IBasicProperties? sourceProps, int retryCount)
    {
        var props = _channel!.CreateBasicProperties();
        props.Persistent = true;
        props.ContentType = sourceProps?.ContentType ?? "application/json";

        var headers = new Dictionary<string, object>();
        if (sourceProps?.Headers is not null)
        {
            foreach (var kv in sourceProps.Headers)
                headers[kv.Key] = kv.Value;
        }

        headers["x-retry-count"] = retryCount;
        headers["x-error-at"] = DateTime.UtcNow.ToString("O");
        props.Headers = headers;

        return props;
    }
}