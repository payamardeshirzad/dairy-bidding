using System.Text;
using System.Text.Json;
using DairyBidding.BiddingService.Messaging.Handlers;
using DairyBidding.Contracts.Events;
using DairyBidding.SharedKernel.Messaging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace DairyBidding.BiddingService.Messaging;

public class AuctionStatusChangedConsumer : BackgroundService
{
    private readonly ILogger<AuctionStatusChangedConsumer> _logger;
    private readonly RabbitMqOptions _options;
    private readonly IServiceScopeFactory _scopeFactory;

    private IConnection? _connection;
    private IChannel? _channel;

    private const string MainExchange = "auction.events";
    private const string MainQueue = "bidding.auction-status";
    private const string MainRoutingKey = "auction.status.changed";

    private const string RetryExchange = "auction.events.retry";
    private const string RetryQueue = "bidding.auction-status.retry";
    private const string RetryRoutingKey = "auction.status.changed.retry";

    private const string DlxExchange = "auction.events.dlx";
    private const string DlqQueue = "bidding.auction-status.dlq";
    private const string DlqRoutingKey = "auction.status.changed.dlq";

    private const int RetryDelayMs = 5000;
    private const int MaxRetries = 3;

    public AuctionStatusChangedConsumer(
        IOptions<RabbitMqOptions> options,
        ILogger<AuctionStatusChangedConsumer> logger,
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

        _logger.LogInformation("AuctionStatusChangedConsumer connected, listening on queue '{Queue}'", MainQueue);
        await base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_channel is null) throw new InvalidOperationException("RabbitMQ channel not initialized.");

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
                var evt = JsonSerializer.Deserialize<AuctionStatusChangedEvent>(json)
                          ?? throw new InvalidOperationException("Invalid AuctionStatusChangedEvent payload.");

                using var scope = _scopeFactory.CreateScope();
                var handler = scope.ServiceProvider.GetRequiredService<IMessageHandler<AuctionStatusChangedEvent>>();
                await handler.HandleAsync(evt, stoppingToken);

                await _channel.BasicAckAsync(ea.DeliveryTag, false, stoppingToken);

                _logger.LogInformation("AuctionStatusChanged processed. AuctionId={AuctionId}, Status={Status}",
                    evt.AuctionId, evt.Status);
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
                            "AuctionStatusChanged processing failed. Requeued to RETRY. RetryAttempt={RetryAttempt}/{MaxRetries}, DeliveryTag={DeliveryTag}",
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
                            "AuctionStatusChanged processing failed. Sent to DLQ after max retries. Retries={Retries}, DeliveryTag={DeliveryTag}",
                            currentRetry, ea.DeliveryTag);
                    }

                    await _channel!.BasicAckAsync(ea.DeliveryTag, false, stoppingToken);
                }
                catch (Exception republishEx)
                {
                    _logger.LogError(republishEx,
                        "Failed to republish AuctionStatusChanged to retry/DLQ. Nacking with requeue=true. DeliveryTag={DeliveryTag}",
                        ea.DeliveryTag);

                    await _channel!.BasicNackAsync(ea.DeliveryTag, false, requeue: true, cancellationToken: stoppingToken);
                }
            }
        };

        await _channel.BasicConsumeAsync(
            queue: MainQueue,
            autoAck: false,
            consumer: consumer,
            cancellationToken: stoppingToken);

        _logger.LogInformation("AuctionStatusChangedConsumer started. Waiting for messages on queue '{Queue}'", MainQueue);

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Graceful shutdown requested
        }
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
