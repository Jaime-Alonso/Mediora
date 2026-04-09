# Getting Started

This page shows the minimum setup to run Mediora in a .NET application.

## Install

```bash
dotnet add package Mediora --prerelease
dotnet add package Mediora.Contracts --prerelease
```

## Register Mediora

```csharp
using Mediora;
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();

services.AddMediora(options =>
{
    options.RegisterServicesFromAssembly(typeof(Program).Assembly);
});
```

## Request/response example

```csharp
public sealed record Ping(string Message) : IRequest<Pong>;

public sealed record Pong(string Message);

public sealed class PingHandler : IRequestHandler<Ping, Pong>
{
    public Task<Pong> Handle(Ping request, CancellationToken cancellationToken)
        => Task.FromResult(new Pong($"Pong: {request.Message}"));
}
```

## Send and publish

```csharp
public sealed class OrdersController : ControllerBase
{
    private readonly IMediator _mediator;

    public OrdersController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpPost("orders")]
    public async Task<IActionResult> Create(CancellationToken cancellationToken)
    {
        Pong response = await _mediator.Send(new Ping("hello"), cancellationToken);
        await _mediator.Publish(new UserCreatedNotification("user-123"), cancellationToken);

        return Ok(response);
    }
}
```

## Stream request example

```csharp
public sealed record NumberStream(int Count) : IStreamRequest<int>;

public sealed class NumberStreamHandler : IStreamRequestHandler<NumberStream, int>
{
    public async IAsyncEnumerable<int> Handle(
        NumberStream request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        for (int i = 0; i < request.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return i;
            await Task.Yield();
        }
    }
}
```

## Related

- [Home](Home.md)
- [DI Registration](DI-Registration.md)
- [Pipeline Behaviors](Pipeline-Behaviors.md)
