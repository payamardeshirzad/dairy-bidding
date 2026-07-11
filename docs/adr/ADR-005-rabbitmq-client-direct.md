# ADR-005: RabbitMQ.Client directly, no abstraction layer (initial implementation)

## Status
Accepted — Superseded by ADR-037

## Context
The platform needs async event delivery between services. Abstraction frameworks (MassTransit, NServiceBus, Rebus) were considered but the initial slice was deliberately simple to reduce onboarding cost and maintain full visibility of the message topology.

## Decision
Use raw **RabbitMQ.Client 7.0.0** with a hand-rolled publisher/consumer pattern:

- Topic exchange per domain (`bids`, `auctions`)
- `BidPlacedPublisher` wraps `IChannel.BasicPublish`
- `BidPlacedConsumer` is a `BackgroundService` wrapping `BasicConsume`
- 3-exchange retry/DLQ topology: `work → retry (with TTL) → dlq`
- `MaxRetries = 3`, `RetryDelayMs = 5000`

**Rejected**: MassTransit (additional abstraction layer), NServiceBus (commercial licence), Rebus (less active community).

## Consequences
- (+) Full visibility of exchange/queue declarations in code — no framework magic
- (+) Easier to audit security properties of the message topology
- (-) ~600 lines of hand-rolled consumer/publisher/retry/DLQ boilerplate spread across services
- (-) No transactional outbox: a crash between the DB write and `BasicPublish` silently loses the event
- (-) No saga support for multi-step workflows
- **Superseded by ADR-037 (MassTransit) which closes all these gaps**
