# Mediora.Contracts

`Mediora.Contracts` contains the core mediator contracts used by Mediora-based applications.

Use this package when you want to reference request, notification, stream, and pipeline abstractions without taking a dependency on the full `Mediora` implementation package.

## Install

```bash
dotnet add package Mediora.Contracts
```

## Included abstractions

- `IRequest<TResponse>` and `IRequest`
- `IRequestHandler<TRequest, TResponse>` and `IRequestHandler<TRequest>`
- `INotification` and `INotificationHandler<TNotification>`
- `IStreamRequest<TResponse>` and `IStreamRequestHandler<TRequest, TResponse>`
- `IPipelineBehavior<TRequest, TResponse>`
- `IStreamPipelineBehavior<TRequest, TResponse>`
- `ISender`, `IPublisher`, and `IMediator`

## Example

```csharp
using Mediora;

public sealed record CreateOrder(Guid CustomerId, decimal Total) : IRequest<Guid>;

public sealed class CreateOrderHandler : IRequestHandler<CreateOrder, Guid>
{
    public Task<Guid> Handle(CreateOrder request, CancellationToken cancellationToken)
    {
        // Application logic here
        return Task.FromResult(Guid.NewGuid());
    }
}
```

## Related package

If you need runtime dispatching and DI integration, install `Mediora`.

## License

MIT
