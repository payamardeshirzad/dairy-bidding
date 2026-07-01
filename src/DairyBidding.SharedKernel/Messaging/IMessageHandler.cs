namespace DairyBidding.SharedKernel.Messaging;

public interface IMessageHandler<in T>
{
    Task HandleAsync(T message, CancellationToken ct = default);
}
