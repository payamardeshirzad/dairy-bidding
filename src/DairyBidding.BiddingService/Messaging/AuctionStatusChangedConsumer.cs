using DairyBidding.BiddingService.Messaging.Handlers;
using DairyBidding.Contracts.Events;
using DairyBidding.SharedKernel.Messaging;
using MassTransit;

namespace DairyBidding.BiddingService.Messaging;

public class AuctionStatusChangedConsumer(IMessageHandler<AuctionStatusChangedEvent> handler) : IConsumer<AuctionStatusChangedEvent>
{
    public Task Consume(ConsumeContext<AuctionStatusChangedEvent> context)
        => handler.HandleAsync(context.Message, context.CancellationToken);
}
