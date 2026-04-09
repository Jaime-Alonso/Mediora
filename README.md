# Mediora - Lightweight Mediator Library for .NET (MediatR Alternative)

[![NuGet Version](https://img.shields.io/nuget/vpre/Mediora)](https://www.nuget.org/packages/Mediora)
[![NuGet Downloads](https://img.shields.io/nuget/dt/Mediora)](https://www.nuget.org/packages/Mediora)
[![NuGet Contracts](https://img.shields.io/nuget/vpre/Mediora.Contracts)](https://www.nuget.org/packages/Mediora.Contracts)
![.NET](https://img.shields.io/badge/.NET-6%2B-blue)
![License](https://img.shields.io/github/license/Jaime-Alonso/Mediora)
![MediatR Alternative](https://img.shields.io/badge/MediatR-alternative-blueviolet)

---

**Mediora** is a lightweight, high-performance mediator library for .NET, designed as an alternative to MediatR.

It implements the **Mediator pattern** and supports **CQRS**, enabling clean separation of concerns in modern .NET applications.

### Key Features

- Request/Response pattern
- Pipeline behaviors
- CQRS support
- Dependency Injection integration
- Minimal overhead and high performance

It provides:

- Request/response handlers (`IRequest<TResponse>`, `IRequestHandler<TRequest, TResponse>`)
- Void requests (`IRequest`, `IRequestHandler<TRequest>`)
- Notifications (`INotification`, `INotificationHandler<TNotification>`)
- Stream requests (`IStreamRequest<TResponse>`, `IStreamRequestHandler<TRequest, TResponse>`)
- Pipeline behaviors for request and stream flows

## Packages

- `Mediora.Contracts`: Includes the public contracts (`IRequest`, `IStreamRequest`, handlers, notifications, sender/mediator abstractions, pipeline contracts, and `Unit`) without runtime/DI implementation.
- `Mediora`: Includes runtime dispatching and DI registration (`Mediator`, `AddMediora(...)`) and depends on `Mediora.Contracts`.

## Choosing ISender, IPublisher, or IMediator

- Use `ISender` when a component only sends request/response messages
- Use `IPublisher` when a component only publishes notifications/events
- Use `IMediator` only when a component genuinely needs both capabilities

## Install

```bash
dotnet add package Mediora
dotnet add package Mediora.Contracts
```

## Documentation (Wiki)

- Wiki Home: <https://github.com/Jaime-Alonso/Mediora/wiki>
- Getting Started: <https://github.com/Jaime-Alonso/Mediora/wiki/Getting-Started>
- DI Registration: <https://github.com/Jaime-Alonso/Mediora/wiki/DI-Registration>
- Pipeline Behaviors: <https://github.com/Jaime-Alonso/Mediora/wiki/Pipeline-Behaviors>
- Notification Publish Modes: <https://github.com/Jaime-Alonso/Mediora/wiki/Notification-Publish-Modes>
- Caching: <https://github.com/Jaime-Alonso/Mediora/wiki/Caching>

## Notification publish modes

Mediora supports three notification publishing modes, with per-notification-type overrides.

### Default behavior

- Default mode: `NotificationPublishMode.SequentialFailFast`
- `SequentialFailFast` preserves registration order, executes one handler at a time, and stops on the first failure.
- `SequentialAggregateAll` preserves registration order, executes one handler at a time, and aggregates failures.
- `ParallelAggregateAll` executes handlers concurrently and aggregates failures.
- For configuration and real examples, see the wiki page: <https://github.com/Jaime-Alonso/Mediora/wiki/Notification-Publish-Modes>

## Basic usage

```csharp
using Mediora;
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();

services.AddMediora(options =>
{
    options.RegisterServicesFromAssembly(typeof(Program).Assembly);
});
```

### Request example

```csharp
public sealed record Ping(string Message) : IRequest<Pong>;

public sealed record Pong(string Message);

public sealed class PingHandler : IRequestHandler<Ping, Pong>
{
    public Task<Pong> Handle(Ping request, CancellationToken cancellationToken)
        => Task.FromResult(new Pong($"Pong: {request.Message}"));
}
```

## Adding pipeline behaviors

Pipeline behaviors let you wrap handler execution (for validation, logging, metrics, retries, transactions, and similar cross-cutting concerns).

### Request behavior example

```csharp
public sealed class LoggingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        Console.WriteLine($"[before] {typeof(TRequest).Name}");

        TResponse response = await next();

        Console.WriteLine($"[after] {typeof(TRequest).Name}");
        return response;
    }
}
```

If a behavior does not call `next()`, the pipeline is short-circuited and the handler (and inner behaviors) are not executed.

### Stream behavior example

```csharp
public sealed class StreamLoggingBehavior<TRequest, TResponse> : IStreamPipelineBehavior<TRequest, TResponse>
    where TRequest : IStreamRequest<TResponse>
{
    public async IAsyncEnumerable<TResponse> Handle(
        TRequest request,
        StreamHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        Console.WriteLine($"[before-stream] {typeof(TRequest).Name}");

        await foreach (TResponse item in next().WithCancellation(cancellationToken))
        {
            yield return item;
        }

        Console.WriteLine($"[after-stream] {typeof(TRequest).Name}");
    }
}
```

### How behaviors are registered

Behaviors are registered explicitly inside `AddMediora(...)` so their execution order is intentional and easy to control:

```csharp
services.AddMediora(options =>
{
    options.RegisterServicesFromAssembly(typeof(Program).Assembly);

    // Request pipeline (open generic)
    options.AddOpenBehavior(typeof(LoggingBehavior<,>));

    // Stream pipeline (open generic)
    options.AddOpenStreamBehavior(typeof(StreamLoggingBehavior<,>));
});
```

Closed behaviors are also supported:

```csharp
services.AddMediora(options =>
{
    options.RegisterServicesFromAssembly(typeof(Program).Assembly);
    options.AddBehavior<MyClosedRequestBehavior>();
    options.AddStreamBehavior<MyClosedStreamBehavior>();
});
```

### Execution order

- Behaviors execute in registration order.
- The first registered behavior is the outer wrapper.
- The last registered behavior is the innermost one, closest to the handler.
- The same ordering rules apply to both request and stream pipelines.

### Common pitfalls

- Behaviors are not discovered by assembly scanning. Register them explicitly with `AddOpenBehavior` / `AddOpenStreamBehavior` (or `AddBehavior` / `AddStreamBehavior` for closed types).
- The behavior generic constraints do not match (`IRequest<TResponse>` / `IStreamRequest<TResponse>`).
- The behavior is registered for a different request/response pair than the one being sent.

For deeper guidance and short-circuit examples, see: <https://github.com/Jaime-Alonso/Mediora/wiki/Pipeline-Behaviors>

## Release stage

Current pre-release stream: `0.1.0-rc.*`

## Performance note: collection handling in wrappers

In Mediora wrappers, collection handling follows a deliberate three-path strategy to reduce allocations in common runtime paths.

- `is T[]` (fast path): with `Microsoft.Extensions.DependencyInjection`, `IEnumerable<T>` is typically resolved as `T[]`. Mediora uses the array directly (indexed access) and short-circuits quickly for empty collections.
- `as IList<T>` (medium path): if a container returns `List<T>`/`IList<T>`, Mediora reuses it directly.
- `[.. items]` (materialization path): Mediora only materializes when the sequence is neither array nor `IList<T>`.

This pattern is used in request pipeline behaviors, stream pipeline behaviors, and notification handler collections.

For request and stream pipelines, continuation delegates are built lazily: deeper `next` delegates are only allocated when the current behavior invokes `next()`. This avoids eagerly building the full chain when a behavior short-circuits.

## License

MIT
