# Mediora

[![CI](https://github.com/Jaime-Alonso/Mediora/actions/workflows/ci.yml/badge.svg)](https://github.com/Jaime-Alonso/Mediora/actions/workflows/ci.yml)
[![NuGet Mediora](https://img.shields.io/nuget/vpre/Mediora)](https://www.nuget.org/packages/Mediora)
[![NuGet Mediora.Contracts](https://img.shields.io/nuget/vpre/Mediora.Contracts)](https://www.nuget.org/packages/Mediora.Contracts)

Mediora is a free and open-source mediator library for .NET.
It is inspired by the MediatR programming model and focused on long-term reliability, clean architecture support, and strong engineering practices.

The goal is not to replicate every MediatR feature immediately. The initial focus is a stable, predictable core that teams can trust in production.

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
dotnet add package Mediora --prerelease
dotnet add package Mediora.Contracts --prerelease
```

## DI Registration Behavior

Mediora uses `AddMediora(...)` with assembly scanning for handlers and behaviors.

- `IRequestHandler<TRequest, TResponse>` must be unique per request/response pair
- `IStreamRequestHandler<TRequest, TResponse>` must be unique per stream request/response pair
- Registration is fail-fast: duplicate single-handler contracts from scanning or existing manual registrations cause an `InvalidOperationException` at startup
- Multiple `INotificationHandler<TNotification>` registrations are supported
- Notification publishing is sequential by default (registration order), with optional per-notification parallel overrides
- If one or more notification handlers fail, `Publish` throws an `AggregateException` containing all handler exceptions
- If no notification handlers are registered for a notification type, `Publish` completes successfully
- Multiple `IPipelineBehavior<TRequest, TResponse>` registrations are supported and execute in registration order
- Multiple `IStreamPipelineBehavior<TRequest, TResponse>` registrations are supported and execute in registration order
- Assembly scan cache is scoped to each `AddMediora(...)` configuration
- Runtime wrapper caches are bounded and configurable (`Max*Wrappers`, `MaxWrapperFactories`, optional `WrapperCacheSlidingExpiration` / `WrapperCacheAbsoluteExpiration`)

## Manual registrations and conflict detection

For single-handler contracts (`IRequestHandler<TRequest, TResponse>`, `IRequestHandler<TRequest>`, `IStreamRequestHandler<TRequest, TResponse>`), Mediora validates uniqueness across both:

- handlers discovered by assembly scanning
- handlers already registered in `IServiceCollection`

`AddMediora(...)` throws `InvalidOperationException` when multiple implementations are detected for the same closed contract.

Validation is strict for `ImplementationFactory` registrations: factory-based registrations are treated as conflicting implementations for the same closed contract.

## Notification publish modes

Mediora supports three notification publishing modes, with per-notification-type overrides.

### Default behavior

- Default mode: `NotificationPublishMode.SequentialFailFast`
- `SequentialFailFast` preserves registration order, executes one handler at a time, and stops on the first failure.
- `SequentialAggregateAll` preserves registration order, executes one handler at a time, and aggregates failures.
- `ParallelAggregateAll` executes handlers concurrently and aggregates failures.

### Configuration API

Configure the mode through `AddMediora(...)`:

```csharp
services.AddMediora(options =>
{
    options.RegisterServicesFromAssembly(typeof(Program).Assembly);

    options.DefaultNotificationPublishMode = NotificationPublishMode.SequentialFailFast;
    options.NotificationParallelMaxDegreeOfParallelism = 8;

    options.ConfigureNotificationPublishMode<OrderPlacedEvent>(NotificationPublishMode.ParallelAggregateAll);
    options.ConfigureNotificationPublishMode<EmailQueuedEvent>(NotificationPublishMode.ParallelAggregateAll);
    options.ConfigureNotificationPublishMode<CriticalAuditEvent>(NotificationPublishMode.SequentialAggregateAll);
});
```

### Resolution rules

- If a notification type has an explicit mode override, that override is used.
- Otherwise, `DefaultNotificationPublishMode` is used.
- Mode overrides are exact by notification type.
- The default is `NotificationPublishMode.SequentialFailFast`.

### Mode semantics

- `SequentialFailFast`
  - Executes handlers one by one in registration order.
  - Stops on the first exception and propagates the original exception.

- `SequentialAggregateAll`
  - Executes handlers one by one in registration order.
  - Continues after failures and throws `AggregateException` after all handlers run.

- `ParallelAggregateAll`
  - Executes handlers concurrently.
  - Does not guarantee completion order between handlers.
  - Throws `AggregateException` when one or more handlers fail.

- Intended for independent, I/O-bound handlers.
- Supports optional bounded concurrency through `NotificationParallelMaxDegreeOfParallelism`.

### What `NotificationParallelMaxDegreeOfParallelism` controls

- Applies only when the effective mode is `NotificationPublishMode.ParallelAggregateAll`.
- Limits how many handlers for a single `Publish(...)` call can run concurrently.
- `null` means no explicit limit (all handlers for that publish can run at once).
- `1` effectively serializes execution for that publish operation, even in parallel mode.
- Values greater than the number of handlers behave the same as "no practical limit" for that call.
- It does not enforce a global app-wide throttle across different notification publishes.

Examples:

- 10 handlers + `NotificationParallelMaxDegreeOfParallelism = 3` => up to 3 handlers in flight for that notification publish.
- 10 handlers + `NotificationParallelMaxDegreeOfParallelism = null` => up to 10 handlers in flight for that notification publish.
- 10 handlers + `NotificationParallelMaxDegreeOfParallelism = 1` => handlers run one at a time for that notification publish.

### Validation rules

- `NotificationParallelMaxDegreeOfParallelism` must be `null` or greater than zero.
- `ConfigureNotificationPublishMode<TNotification>(...)` requires `TNotification : INotification`.
- Type-based overloads reject null types and non-`INotification` types.

### Guidance

- Use `SequentialFailFast` for workflows with strong step dependencies.
- Use `SequentialAggregateAll` when ordering matters and you still want best-effort execution.
- Use `ParallelAggregateAll` for independent integrations such as email, webhooks, or external API fan-out.
- Mix modes by notification type so each event flow matches its reliability/performance needs.

## Runtime cache configuration

Mediora caches runtime wrappers/factories to avoid repeating reflection on hot paths (`Send`, `Publish`, `CreateStream`).

Defaults:

- `EnableWrapperCaching = true`
- `MaxRequestWrappers = 2048`
- `MaxStreamRequestWrappers = 1024`
- `MaxNotificationWrappers = 2048`
- `MaxWrapperFactories = 2048`
- `WrapperCacheSlidingExpiration = null` (disabled)
- `WrapperCacheAbsoluteExpiration = null` (disabled)

Cache entries are bounded by the `Max*` limits. If you configure expirations, an entry expires when any configured policy is met.

```csharp
services.AddMediora(options =>
{
    options.RegisterServicesFromAssembly(typeof(Program).Assembly);

    options.MaxRequestWrappers = 2048;
    options.MaxStreamRequestWrappers = 1024;
    options.MaxNotificationWrappers = 2048;
    options.MaxWrapperFactories = 2048;

    options.WrapperCacheSlidingExpiration = TimeSpan.FromMinutes(30);
    options.WrapperCacheAbsoluteExpiration = TimeSpan.FromHours(6);
});
```

You can disable runtime wrapper caching entirely:

```csharp
services.AddMediora(options =>
{
    options.EnableWrapperCaching = false;
    options.RegisterServicesFromAssembly(typeof(Program).Assembly);
});
```

Validation rules:

- All `Max*` values must be greater than zero.
- `WrapperCacheSlidingExpiration` and `WrapperCacheAbsoluteExpiration`, when set, must be greater than `TimeSpan.Zero`.

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

## Release stage

Current pre-release stream: `0.1.0-rc.*`

## Performance note: behavior collection handling

In the internal wrappers, the three-level pattern used for behavior collections is deliberate.

- `is T[]` (fast path): the Microsoft DI container (`Microsoft.Extensions.DependencyInjection`) typically resolves `IEnumerable<T>` as `T[]`. When this happens, Mediora uses the array directly (indexed access, no extra allocations) and can short-circuit quickly on `Length == 0`.
- `as IList<T>` (medium path): if another container returns `List<T>`/`IList<T>`, Mediora reuses it directly and avoids materializing a copy.
- `[.. behaviors]` (slow path): Mediora only materializes when the sequence is neither array nor `IList<T>`.

This avoids unnecessary allocations in common runtime paths and is preferred to a simplified two-step approach that would force materialization for non-array `IList<T>` implementations.

For request and stream pipelines, continuation delegates are built lazily: Mediora allocates deeper `next` delegates only when the current behavior invokes `next()`. This avoids eagerly building the full delegate chain when a behavior short-circuits.

## CI and releases

- Pull requests run restore, build, test, and pack automatically.
- Release tags matching `v*` run validation and publish packages to NuGet.

## License

MIT
