# Mediora

`Mediora` is a lightweight mediator library for .NET focused on predictable behavior and clean architecture workflows.

Use this package when you want request/response, notifications, streaming requests, and pipeline behaviors with DI integration.

## Install

```bash
dotnet add package Mediora
```

## Quick start

```csharp
using Mediora;
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();

// Register Mediora handlers from your assembly
services.AddMediora(cfg => cfg.RegisterServicesFromAssemblyContaining<CreateOrderHandler>());

var provider = services.BuildServiceProvider();
var mediator = provider.GetRequiredService<IMediator>();

var orderId = await mediator.Send(new CreateOrder(Guid.NewGuid(), 120m));
```

## Contracts package

`Mediora` depends on `Mediora.Contracts` and will bring it transitively when installed.

## License

MIT
