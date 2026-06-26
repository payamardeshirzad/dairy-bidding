using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace DairyBidding.BiddingService.Messaging;

public interface IBidPlacedPublisher
{
    void Publish(BidPlacedEvent evt);
}

public class BidPlacedPublisher : IBidPlacedPublisher, IDisposable
{
    private readonly IConnection _connection;
    private readonly IModel _channel;

    private const string Exchange = "bidding.events";
    private const string Queue = "bidding.bidplaced";
    private const string RoutingKey = "bid.placed";

    public BidPlacedPublisher(IOptions<RabbitMqOptions> options)
    {
        var o = options.Value;

        var factory = new ConnectionFactory
        {
            HostName = o.Host,
            Port = o.Port,
            UserName = o.User,
            Password = o.Pass,
            VirtualHost = o.VHost,
            RequestedConnectionTimeout = TimeSpan.FromSeconds(10),
            SocketReadTimeout = TimeSpan.FromSeconds(10),
            SocketWriteTimeout = TimeSpan.FromSeconds(10),
            AutomaticRecoveryEnabled = true
        };
        Console.WriteLine($"Connecting to RabbitMQ at {o.Host}:{o.Port} with user '{o.User}', Password '{o.Pass}' and vhost '{o.VHost}'");
        try
        {
            _connection = factory.CreateConnection();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to connect to RabbitMQ: {ex.Message}");
            throw;
        }
        Console.WriteLine($"Connected to RabbitMQ at {o.Host}:{o.Port} with user '{o.User}' and vhost '{o.VHost}'");

        _channel = _connection.CreateModel();

        _channel.ExchangeDeclare(exchange: Exchange, type: ExchangeType.Topic, durable: true, autoDelete: false);
        _channel.QueueDeclare(queue: Queue, durable: true, exclusive: false, autoDelete: false);
        _channel.QueueBind(queue: Queue, exchange: Exchange, routingKey: RoutingKey);
    }

    public void Publish(BidPlacedEvent evt)
    {
        var json = JsonSerializer.Serialize(evt);
        var body = Encoding.UTF8.GetBytes(json);

        var props = _channel.CreateBasicProperties();
        props.DeliveryMode = 2; // persistent
        props.ContentType = "application/json";

        _channel.BasicPublish(
            exchange: Exchange,
            routingKey: RoutingKey,
            basicProperties: props,
            body: body);
    }

    public void Dispose()
    {
        Console.WriteLine("Disposing RabbitMQ connection and channel");
        _channel?.Dispose();
        _connection?.Dispose();
    }
}