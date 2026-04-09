# Notification Publish Modes

Mediora supports three notification publish modes and allows per-notification-type overrides.

## Default mode

- `NotificationPublishMode.SequentialFailFast` is the default.

## Configuration

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

## Resolution rules

- If a notification type has an explicit override, Mediora uses it.
- Otherwise, Mediora uses `DefaultNotificationPublishMode`.
- Overrides are exact by notification type.

## Mode semantics

### `SequentialFailFast`

- Executes handlers one by one in registration order.
- Stops on first failure and propagates the original exception.

Ideal functional case:

- A flow where each notification handler depends on previous side effects.
- If one step fails, there is no value in continuing.
- Fail-fast behavior prevents partial chained execution.

Example with handler names and emitted notifications:

- Publisher handler: `PlaceOrderHandler` publishes `OrderPlacedNotification`.
- Notification handlers in order:
  - `ReserveStockNotificationHandler`
  - `CreateShipmentNotificationHandler`
  - `SendOrderConfirmationNotificationHandler`
- If `ReserveStockNotificationHandler` fails, the publish operation stops there.

Code example:

```csharp
services.AddMediora(options =>
{
    options.RegisterServicesFromAssembly(typeof(Program).Assembly);
    options.ConfigureNotificationPublishMode<OrderPlacedNotification>(
        NotificationPublishMode.SequentialFailFast);
});

public sealed record PlaceOrderCommand(string OrderId) : IRequest;
public sealed record OrderPlacedNotification(string OrderId) : INotification;

public sealed class PlaceOrderHandler : IRequestHandler<PlaceOrderCommand>
{
    private readonly IPublisher _publisher;

    public PlaceOrderHandler(IPublisher publisher) => _publisher = publisher;

    public async Task Handle(PlaceOrderCommand request, CancellationToken cancellationToken)
    {
        await _publisher.Publish(new OrderPlacedNotification(request.OrderId), cancellationToken);
    }
}

public sealed class ReserveStockNotificationHandler : INotificationHandler<OrderPlacedNotification>
{
    public Task Handle(OrderPlacedNotification notification, CancellationToken cancellationToken)
        => throw new InvalidOperationException("Stock unavailable.");
}

public sealed class CreateShipmentNotificationHandler : INotificationHandler<OrderPlacedNotification>
{
    public Task Handle(OrderPlacedNotification notification, CancellationToken cancellationToken)
        => Task.CompletedTask;
}
```

Notes:

- If `ReserveStockNotificationHandler` fails, `CreateShipmentNotificationHandler` is not executed.
- This mode is useful when downstream steps depend on upstream success.

### `SequentialAggregateAll`

- Executes handlers one by one in registration order.
- Continues after failures.
- Throws `AggregateException` after all handlers run when one or more failed.

Ideal functional case:

- A flow where execution order matters, but each step should still be attempted.
- You want best effort while keeping deterministic order.
- Aggregated errors provide a complete failure report at the end.

Example with handler names and emitted notifications:

- Publisher handler: `CloseBillingCycleHandler` publishes `BillingCycleClosedNotification`.
- Notification handlers in order:
  - `GenerateInvoiceNotificationHandler`
  - `ArchiveInvoiceNotificationHandler`
  - `NotifyAccountingNotificationHandler`
- If `GenerateInvoiceNotificationHandler` fails, the next handlers still run.
- After all handlers run, Mediora throws an `AggregateException` if one or more failed.

Code example:

```csharp
services.AddMediora(options =>
{
    options.RegisterServicesFromAssembly(typeof(Program).Assembly);
    options.ConfigureNotificationPublishMode<BillingCycleClosedNotification>(
        NotificationPublishMode.SequentialAggregateAll);
});

public sealed record CloseBillingCycleCommand(string CycleId) : IRequest;
public sealed record BillingCycleClosedNotification(string CycleId) : INotification;

public sealed class CloseBillingCycleHandler : IRequestHandler<CloseBillingCycleCommand>
{
    private readonly IPublisher _publisher;

    public CloseBillingCycleHandler(IPublisher publisher) => _publisher = publisher;

    public async Task Handle(CloseBillingCycleCommand request, CancellationToken cancellationToken)
    {
        await _publisher.Publish(new BillingCycleClosedNotification(request.CycleId), cancellationToken);
    }
}

public sealed class GenerateInvoiceNotificationHandler : INotificationHandler<BillingCycleClosedNotification>
{
    public Task Handle(BillingCycleClosedNotification notification, CancellationToken cancellationToken)
        => throw new InvalidOperationException("PDF generation failed.");
}

public sealed class ArchiveInvoiceNotificationHandler : INotificationHandler<BillingCycleClosedNotification>
{
    public Task Handle(BillingCycleClosedNotification notification, CancellationToken cancellationToken)
        => Task.CompletedTask;
}
```

Notes:

- `ArchiveInvoiceNotificationHandler` still runs even if `GenerateInvoiceNotificationHandler` fails.
- After all handlers complete, Mediora throws one `AggregateException` containing all failures.

### `ParallelAggregateAll`

- Executes handlers concurrently.
- Completion order is not guaranteed.
- Throws `AggregateException` when one or more handlers fail.

Ideal functional case:

- An event fan-out where handlers are independent.
- Most handlers are I/O bound and can run safely at the same time.
- Parallel execution reduces end-to-end latency.

Example with handler names and emitted notifications:

- Publisher handler: `RegisterUserHandler` publishes `UserRegisteredNotification`.
- Notification handlers executed concurrently:
  - `SendWelcomeEmailNotificationHandler`
  - `TrackSignupAnalyticsNotificationHandler`
  - `ProvisionCrmContactNotificationHandler`
- Mediora waits for all handlers and then throws an `AggregateException` if one or more failed.

Code example:

```csharp
services.AddMediora(options =>
{
    options.RegisterServicesFromAssembly(typeof(Program).Assembly);
    options.ConfigureNotificationPublishMode<UserRegisteredNotification>(
        NotificationPublishMode.ParallelAggregateAll);
    options.NotificationParallelMaxDegreeOfParallelism = 4;
});

public sealed record RegisterUserCommand(string UserId) : IRequest;
public sealed record UserRegisteredNotification(string UserId) : INotification;

public sealed class RegisterUserHandler : IRequestHandler<RegisterUserCommand>
{
    private readonly IPublisher _publisher;

    public RegisterUserHandler(IPublisher publisher) => _publisher = publisher;

    public async Task Handle(RegisterUserCommand request, CancellationToken cancellationToken)
    {
        await _publisher.Publish(new UserRegisteredNotification(request.UserId), cancellationToken);
    }
}

public sealed class SendWelcomeEmailNotificationHandler : INotificationHandler<UserRegisteredNotification>
{
    public async Task Handle(UserRegisteredNotification notification, CancellationToken cancellationToken)
    {
        await Task.Delay(100, cancellationToken);
    }
}

public sealed class TrackSignupAnalyticsNotificationHandler : INotificationHandler<UserRegisteredNotification>
{
    public async Task Handle(UserRegisteredNotification notification, CancellationToken cancellationToken)
    {
        await Task.Delay(100, cancellationToken);
    }
}
```

Notes:

- Notification handlers run concurrently, so completion order is not guaranteed.
- Failures are collected and surfaced together as `AggregateException`.

## Parallel degree of parallelism

`NotificationParallelMaxDegreeOfParallelism`:

- Applies only to `ParallelAggregateAll`.
- Limits concurrent handlers for a single `Publish(...)` call.
- `null` means no explicit limit.
- Must be `null` or greater than zero.

## Guidance

- Use `SequentialFailFast` for strongly dependent workflows.
- Use `SequentialAggregateAll` when order matters but best-effort execution is needed.
- Use `ParallelAggregateAll` for independent I/O-bound handlers.

## Related

- [Home](Home.md)
- [DI Registration](DI-Registration.md)
