# ADR-011: Service decomposition via extension method pattern

## Status
Accepted — Implemented

## Context
Minimal API `Program.cs` files grow quickly when DI registration, middleware pipeline, and endpoint mapping are all inline. A 300-line `Program.cs` is hard to navigate and makes PR reviews noisy.

## Decision
Each service separates setup into two extension method files, keeping `Program.cs` at ≤30 lines:

- **`ServiceExtensions.cs`** — all `builder.Services.Add*` registrations (DI)
- **`WebAppExtensions.cs`** — all `app.Use*` middleware and `app.Map*` endpoint registrations

```csharp
// Program.cs
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddBiddingServices(builder.Configuration);

var app = builder.Build();
app.UseBiddingMiddleware();
app.Run();
```

**Rejected**: Monolithic `Program.cs` with all configuration inline.

## Consequences
- (+) `Program.cs` is ≤30 lines — easy to read and review
- (+) DI registration logic can be unit-tested in isolation
- (+) Reduced cognitive load when onboarding to a new service
- (-) Two extra files per service; requires discipline to maintain the pattern consistently
