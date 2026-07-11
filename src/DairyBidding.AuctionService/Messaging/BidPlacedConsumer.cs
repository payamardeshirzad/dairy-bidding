using DairyBidding.AuctionService.Messaging.Handlers;
using DairyBidding.Contracts.Events;
using DairyBidding.SharedKernel.Messaging;
using MassTransit;

namespace DairyBidding.AuctionService.Messaging;

public class BidPlacedConsumer(IMessageHandler<BidPlacedEvent> handler) : IConsumer<BidPlacedEvent>
{
    public Task Consume(ConsumeContext<BidPlacedEvent> context)
        => handler.HandleAsync(context.Message, context.CancellationToken);
}
