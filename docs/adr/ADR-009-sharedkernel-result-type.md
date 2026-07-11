# ADR-009: SharedKernel with Result<T>, IMessageHandler<T>, and OptimisticRetry

## Status
Accepted — Partially Implemented (`IMessageHandler<T>` and `OptimisticRetry` done; `Result<T>` stub only)

## Context
Domain primitives that are reused across multiple services — `Result<T>` for explicit error propagation and `IMessageHandler<T>` for message handling abstraction — should not be duplicated per service.

Throwing exceptions for expected failures (validation errors, not-found, business rule violations) is expensive on hot paths and conflates expected outcomes with unexpected faults.

## Decision
**`DairyBidding.SharedKernel`** (`src/DairyBidding.SharedKernel/`) provides:

```csharp
// Result discriminated union — no external dependency
public readonly struct Result<T>
{
    public bool IsSuccess { get; }
    public T Value { get; }
    public string Error { get; }
    public static Result<T> Ok(T value) => ...;
    public static Result<T> Fail(string error) => ...;
    public TOut Match<TOut>(Func<T, TOut> onOk, Func<string, TOut> onFail) => ...;
}

// Handler interface (see ADR-007)
public interface IMessageHandler<in T>
{
    Task HandleAsync(T message, CancellationToken ct);
}
```

**Rejected**: Throwing exceptions for expected failures; third-party Result libraries (LanguageExt, ErrorOr) — adds an external dependency for a simple type.

## Consequences
- (+) Explicit failure handling without exceptions on hot paths
- (+) Lightweight — no external dependency
- (-) `Result<T>` must be populated before services can adopt it — currently a stub
- (-) SharedKernel must have no infrastructure dependencies (no EF Core, no ASP.NET Core packages)
