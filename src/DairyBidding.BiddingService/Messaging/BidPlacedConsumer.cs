using System.Text;
using System.Text.Json;
using DairyBidding.BiddingService.Data;
using Microsoft.EntityFrameworkCore;
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
    private IChannel? _channel;

    private const string MainExchange = "bidding.events";
    private const string MainQueue = "bidding.bidplaced";
    private const string MainRoutingKey = "bid.placed";

    private const string RetryExchange = "bidding.events.retry";
    private const string RetryQueue = "bidding.bidplaced.retry";
    private const string RetryRoutingKey = "bid.placed.retry";

    private const string DlxExchange = "bidding.events.dlx";
    private const string DlqQueue = "bidding.bidplaced.dlq";
    private const string DlqRoutingKey = "bid.placed.dlq";

    private const int RetryDelayMs = 5000;
    private const int MaxRetries = 3;

    public BidPlacedConsumer(
        IOptions<RabbitMqOptions> options,
        ILogger<BidPlacedConsumer> logger,
        IServiceScopeFactory scopeFactory)
    {
        _options = options.Value;
        _logger = logger;
        _scopeFactory = scopeFactory;
    }

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        var factory = new ConnectionFactory
        {
            HostName = _options.Host,
            Port = _options.Port,
            UserName = _options.User,
            Password = _options.Pass,
            VirtualHost = _options.VHost,
            AutomaticRecoveryEnabled = true
        };

        _connection = await factory.CreateConnectionAsync(cancellationToken);
        _channel = await _connection.CreateChannelAsync(cancellationToken: cancellationToken);

        await _channel.ExchangeDeclareAsync(MainExchange, ExchangeType.Topic, durable: true, autoDelete: false, cancellationToken: cancellationToken);
        await _channel.ExchangeDeclareAsync(RetryExchange, ExchangeType.Topic, durable: true, autoDelete: false, cancellationToken: cancellationToken);
        await _channel.ExchangeDeclareAsync(DlxExchange, ExchangeType.Topic, durable: true, autoDelete: false, cancellationToken: cancellationToken);

        await _channel.QueueDeclareAsync(MainQueue, durable: true, exclusive: false, autoDelete: false, cancellationToken: cancellationToken);
        await _channel.QueueBindAsync(MainQueue, MainExchange, MainRoutingKey, cancellationToken: cancellationToken);

        var retryArgs = new Dictionary<string, object?>
        {
            ["x-message-ttl"] = RetryDelayMs,
            ["x-dead-letter-exchange"] = MainExchange,
            ["x-dead-letter-routing-key"] = MainRoutingKey
        };
        await _channel.QueueDeclareAsync(RetryQueue, durable: true, exclusive: false, autoDelete: false, arguments: retryArgs, cancellationToken: cancellationToken);
        await _channel.QueueBindAsync(RetryQueue, RetryExchange, RetryRoutingKey, cancellationToken: cancellationToken);

        await _channel.QueueDeclareAsync(DlqQueue, durable: true, exclusive: false, autoDelete: false, cancellationToken: cancellationToken);
        await _channel.QueueBindAsync(DlqQueue, DlxExchange, DlqRoutingKey, cancellationToken: cancellationToken);

        await _channel.BasicQosAsync(0, 1, false, cancellationToken);

        _logger.LogInformation("BidPlacedConsumer connected to RabbitMQ {Host}:{Port}, vhost {VHost}",
            _options.Host, _options.Port, _options.VHost);

        await base.StartAsync(cancellationToken);
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_channel is null)
            throw new InvalidOperationException("RabbitMQ channel is not initialized.");

        var consumer = new AsyncEventingBasicConsumer(_channel);

        consumer.ReceivedAsync += async (_, ea) =>
        {
            var correlationId = GetCorrelationId(ea.BasicProperties) ?? Guid.NewGuid().ToString("N");

            using var logScope = _logger.BeginScope(new Dictionary<string, object>
            {
                ["CorrelationId"] = correlationId,
                ["MessageId"] = ea.BasicProperties?.MessageId ?? "(none)"
            });

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
                    .AnyAsync(x => x.MessageId == evt.MessageId, stoppingToken);

                if (alreadyProcessed)
                {
                    _logger.LogInformation("Duplicate message skipped. MessageId={MessageId}", evt.MessageId);
                    await _channel.BasicAckAsync(ea.DeliveryTag, false, stoppingToken);
                    return;
                }

                var read = await db.AuctionBidReadModels
                    .FirstOrDefaultAsync(x => x.AuctionId == evt.AuctionId, stoppingToken);

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

                db.ProcessedMessages.Add(new ProcessedMessage
                {
                    MessageId = evt.MessageId,
                    ProcessedAtUtc = DateTime.UtcNow
                });

                await db.SaveChangesAsync(stoppingToken);
                await _channel.BasicAckAsync(ea.DeliveryTag, false, stoppingToken);
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

                        await _channel!.BasicPublishAsync(
                            exchange: RetryExchange,
                            routingKey: RetryRoutingKey,
                            mandatory: false,
                            basicProperties: retryProps,
                            body: ea.Body,
                            cancellationToken: stoppingToken);

                        _logger.LogWarning(ex,
                            "Processing failed. Message requeued to RETRY. RetryAttempt={RetryAttempt}/{MaxRetries}, DeliveryTag={DeliveryTag}",
                            nextRetry, MaxRetries, ea.DeliveryTag);
                    }
                    else
                    {
                        var dlqProps = CreateRepublishProperties(ea.BasicProperties, currentRetry);

                        await _channel!.BasicPublishAsync(
                            exchange: DlxExchange,
                            routingKey: DlqRoutingKey,
                            mandatory: false,
                            basicProperties: dlqProps,
                            body: ea.Body,
                            cancellationToken: stoppingToken);

                        _logger.LogError(ex,
                            "Processing failed. Message sent to DLQ after max retries. Retries={Retries}, DeliveryTag={DeliveryTag}",
                            currentRetry, ea.DeliveryTag);
                    }

                    await _channel!.BasicAckAsync(ea.DeliveryTag, false, stoppingToken);
                }
                catch (Exception republishEx)
                {
                    _logger.LogError(republishEx,
                        "Failed to republish message to retry/DLQ. Nack with requeue=true to avoid loss. DeliveryTag={DeliveryTag}",
                        ea.DeliveryTag);

                    await _channel!.BasicNackAsync(ea.DeliveryTag, false, requeue: true, cancellationToken: stoppingToken);
                }
            }
        };

        _channel.BasicConsumeAsync(
            queue: MainQueue,
            autoAck: false,
            consumer: consumer,
            cancellationToken: stoppingToken);

        _logger.LogInformation("BidPlacedConsumer started. Waiting for messages on queue '{Queue}'", MainQueue);
        return Task.CompletedTask;
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (_channel is not null) await _channel.CloseAsync(cancellationToken);
            if (_connection is not null) await _connection.CloseAsync(cancellationToken);
        }
        catch
        {
            // ignore shutdown exceptions
        }

        await base.StopAsync(cancellationToken);
    }

    public override void Dispose()
    {
        _channel?.Dispose();
        _connection?.Dispose();
        base.Dispose();
    }

    private static string? GetCorrelationId(IReadOnlyBasicProperties? props)
    {
        if (props?.Headers is null) return null;
        if (!props.Headers.TryGetValue("x-correlation-id", out var raw) || raw is null) return null;

        return raw switch
        {
            byte[] b => Encoding.UTF8.GetString(b),
            string s => s,
            _ => raw.ToString()
        };
    }

    private static int GetRetryCount(IReadOnlyBasicProperties? props)
    {
        if (props?.Headers is null) return 0;
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

    private static BasicProperties CreateRepublishProperties(IReadOnlyBasicProperties? sourceProps, int retryCount)
    {
        var headers = new Dictionary<string, object?>();

        if (sourceProps?.Headers is not null)
        {
            foreach (var kv in sourceProps.Headers)
                headers[kv.Key] = kv.Value;
        }

        headers["x-retry-count"] = retryCount;
        headers["x-error-at"] = DateTime.UtcNow.ToString("O");

        return new BasicProperties
        {
            Persistent = true,
            ContentType = sourceProps?.ContentType ?? "application/json",
            MessageId = sourceProps?.MessageId,
            CorrelationId = sourceProps?.CorrelationId,
            Headers = headers
        };
    }
}