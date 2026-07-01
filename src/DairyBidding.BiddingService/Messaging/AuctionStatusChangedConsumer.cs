using System.Text;
using System.Text.Json;
using DairyBidding.BiddingService.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace DairyBidding.BiddingService.Messaging;

public record AuctionStatusChangedEvent(
    string MessageId,
    string AuctionId,
    string Title,
    string Status,
    DateTime StartsAt,
    DateTime EndsAt,
    DateTime ChangedAtUtc
);

public class AuctionStatusChangedConsumer : BackgroundService
{
    private readonly ILogger<AuctionStatusChangedConsumer> _logger;
    private readonly RabbitMqOptions _options;
    private readonly IServiceScopeFactory _scopeFactory;

    private IConnection? _connection;
    private IChannel? _channel;

    private const string Exchange = "auction.events";
    private const string Queue = "bidding.auction-status";
    private const string RoutingKey = "auction.status.changed";

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

        await _channel.ExchangeDeclareAsync(Exchange, ExchangeType.Topic, durable: true, autoDelete: false, cancellationToken: cancellationToken);
        await _channel.QueueDeclareAsync(Queue, durable: true, exclusive: false, autoDelete: false, cancellationToken: cancellationToken);
        await _channel.QueueBindAsync(Queue, Exchange, RoutingKey, cancellationToken: cancellationToken);
        await _channel.BasicQosAsync(0, 1, false, cancellationToken);

        _logger.LogInformation("AuctionStatusChangedConsumer connected, listening on queue '{Queue}'", Queue);
        await base.StartAsync(cancellationToken);
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_channel is null) throw new InvalidOperationException("RabbitMQ channel not initialized.");

        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.ReceivedAsync += async (_, ea) =>
        {
            try
            {
                var json = Encoding.UTF8.GetString(ea.Body.ToArray());
                var evt = JsonSerializer.Deserialize<AuctionStatusChangedEvent>(json)
                          ?? throw new InvalidOperationException("Invalid AuctionStatusChangedEvent payload.");

                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<BiddingDbContext>();

                var existing = await db.AuctionReadModels.FirstOrDefaultAsync(a => a.AuctionId == evt.AuctionId, stoppingToken);
                if (existing is null)
                {
                    db.AuctionReadModels.Add(new AuctionReadModel
                    {
                        AuctionId = evt.AuctionId,
                        Title = evt.Title,
                        Status = evt.Status,
                        StartsAt = evt.StartsAt,
                        EndsAt = evt.EndsAt,
                        UpdatedAtUtc = evt.ChangedAtUtc
                    });
                }
                else
                {
                    existing.Status = evt.Status;
                    existing.Title = evt.Title;
                    existing.StartsAt = evt.StartsAt;
                    existing.EndsAt = evt.EndsAt;
                    existing.UpdatedAtUtc = evt.ChangedAtUtc;
                }

                await db.SaveChangesAsync(stoppingToken);
                await _channel.BasicAckAsync(ea.DeliveryTag, false, stoppingToken);

                _logger.LogInformation("AuctionReadModel updated: AuctionId={AuctionId}, Status={Status}", evt.AuctionId, evt.Status);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process AuctionStatusChanged. Nacking with requeue.");
                await _channel!.BasicNackAsync(ea.DeliveryTag, false, requeue: true, cancellationToken: stoppingToken);
            }
        };

        _channel.BasicConsumeAsync(queue: Queue, autoAck: false, consumer: consumer, cancellationToken: stoppingToken);
        return Task.CompletedTask;
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (_channel is not null) await _channel.CloseAsync(cancellationToken);
            if (_connection is not null) await _connection.CloseAsync(cancellationToken);
        }
        catch { /* ignore shutdown */ }
        await base.StopAsync(cancellationToken);
    }

    public override void Dispose()
    {
        _channel?.Dispose();
        _connection?.Dispose();
        base.Dispose();
    }
}
