# DI Registration

Mediora is registered through `AddMediora(...)`.

## Assembly scanning

`RegisterServicesFromAssembly(...)` scans and registers handlers from the specified assembly.

```csharp
services.AddMediora(options =>
{
    options.RegisterServicesFromAssembly(typeof(Program).Assembly);
});
```

## Single-handler contract uniqueness

For these contracts, Mediora enforces exactly one implementation per closed type:

- `IRequestHandler<TRequest, TResponse>`
- `IRequestHandler<TRequest>`
- `IStreamRequestHandler<TRequest, TResponse>`

If duplicates are discovered (from scan results and/or existing `IServiceCollection` registrations), `AddMediora(...)` throws `InvalidOperationException` at startup.

## Notifications

Multiple `INotificationHandler<TNotification>` registrations are valid and expected.

- No handlers: `Publish(...)` completes successfully.
- One or more handlers fail: behavior depends on the configured publish mode.

See [Notification Publish Modes](Notification-Publish-Modes.md).

## Behaviors are explicit

Pipeline behaviors are not discovered by assembly scan.

Register them explicitly inside `AddMediora(...)` to keep execution order intentional and visible.

```csharp
services.AddMediora(options =>
{
    options.RegisterServicesFromAssembly(typeof(Program).Assembly);

    options.AddOpenBehavior(typeof(LoggingBehavior<,>));
    options.AddOpenStreamBehavior(typeof(StreamLoggingBehavior<,>));
});
```

See [Pipeline Behaviors](Pipeline-Behaviors.md).

## Related

- [Home](Home.md)
- [Getting Started](Getting-Started.md)
- [Pipeline Behaviors](Pipeline-Behaviors.md)
