# ADR-007: IMessageHandler<T> handler extraction pattern

## Status
Accepted — Implemented

## Context
Consumer `BackgroundService` classes that mix transport concerns (channel management, ACK/NACK, retry) with business logic are difficult to unit test. The transport layer requires a live RabbitMQ connection; the business logic does not.

## Decision
Consumer `BackgroundService` classes delegate to `IMessageHandler<T>` implementations resolved from a DI scope:

```csharp
public interface IMessageHandler<in T>
{
    Task HandleAsync(T message, CancellationToken ct);
}
```

- Handlers are registered as `Scoped` in the DI container
- The `BackgroundService` creates a scope per message, resolves `IMessageHandler<T>`, and calls `HandleAsync`
- Transport concerns (channel, ACK, NACK, retry delay) remain in the `BackgroundService`

Defined in `DairyBidding.SharedKernel` (see ADR-009).

**Rejected**: Inline processing logic in the consumer class itself.

## Consequences
- (+) Handlers are independently unit-testable with no broker dependency
- (+) Transport layer and business logic have clear separation of concerns
- (+) New message types require only a new `IMessageHandler<T>` implementation — no changes to the consumer infrastructure
- (-) One extra level of indirection compared to inline logic
