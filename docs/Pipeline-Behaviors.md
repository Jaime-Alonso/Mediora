# Pipeline Behaviors

Pipeline behaviors wrap handler execution for cross-cutting concerns such as logging, validation, metrics, retries, and transactions.

## Understanding `next()`

`next()` is the continuation that moves execution to the next behavior in the pipeline.

- In request behaviors, `next()` returns `Task<TResponse>`.
- In stream behaviors, `next()` returns `IAsyncEnumerable<TResponse>`.

Think of behaviors as nested wrappers:

1. Your behavior runs code before `next()`.
2. `await next()` executes inner behaviors and eventually the handler.
3. Control returns to your behavior so you can run code after `next()`.

If you do not call `next()`, the pipeline is short-circuited.

## Request behavior example

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

### Request short-circuit example

This pattern is useful for in-memory caching or precomputed responses.

```csharp
public sealed class CachedPingBehavior : IPipelineBehavior<Ping, Pong>
{
    private static readonly ConcurrentDictionary<string, Pong> Cache = new();

    public Task<Pong> Handle(
        Ping request,
        RequestHandlerDelegate<Pong> next,
        CancellationToken cancellationToken)
    {
        if (Cache.TryGetValue(request.Message, out Pong cached))
        {
            return Task.FromResult(cached); // next() is not called
        }

        return GetAndCacheAsync(request, next);
    }

    private static async Task<Pong> GetAndCacheAsync(Ping request, RequestHandlerDelegate<Pong> next)
    {
        Pong response = await next();
        Cache[request.Message] = response;
        return response;
    }
}
```

Call `next()` at most once in normal behavior implementations.

## Stream behavior example

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

### Stream short-circuit example

```csharp
public sealed class EmptyStreamForGuestsBehavior : IStreamPipelineBehavior<GetOrdersStream, OrderDto>
{
    public async IAsyncEnumerable<OrderDto> Handle(
        GetOrdersStream request,
        StreamHandlerDelegate<OrderDto> next,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (request.IsGuest)
        {
            yield break; // next() is not called
        }

        await foreach (OrderDto order in next().WithCancellation(cancellationToken))
        {
            yield return order;
        }
    }
}
```

For stream behaviors, remember that calling `next()` only creates the stream. The handler executes when you enumerate it with `await foreach`.

## Registration

Behaviors must be registered explicitly in `AddMediora(...)`.

```csharp
services.AddMediora(options =>
{
    options.RegisterServicesFromAssembly(typeof(Program).Assembly);

    options.AddOpenBehavior(typeof(LoggingBehavior<,>));
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

## Execution order

- Behaviors execute in registration order.
- First registered is the outer wrapper.
- Last registered is the innermost wrapper, closest to the handler.
- Same ordering rules apply to request and stream pipelines.

## Short-circuiting

If a behavior does not call `next()`, inner behaviors and the handler are not executed.

This can be useful for authorization gates, cached responses, or early validation failures.

## Common pitfalls

- Forgetting explicit behavior registration.
- Generic constraints do not match (`IRequest<TResponse>` or `IStreamRequest<TResponse>`).
- Closed behavior type does not match the request/response pair being sent.

## Related

- [Home](Home.md)
- [DI Registration](DI-Registration.md)
- [Getting Started](Getting-Started.md)
