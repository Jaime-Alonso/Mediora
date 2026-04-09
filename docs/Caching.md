# Caching

Mediora caches internal runtime objects so `Send`, `CreateStream`, and `Publish` can avoid repeating reflection work on every call.

This is important for throughput and latency when the same message types are used frequently.

## What is being cached

Mediora caches two kinds of internal objects:

- **Wrappers**: dispatch helpers used to route a concrete request/notification type to the correct handler pipeline.
- **Wrapper factories**: compiled delegates used to instantiate wrapper types efficiently.

These are internal runtime artifacts, not your business data.

## Why it matters

Without cache, Mediora can still work correctly, but it has to rebuild runtime dispatch artifacts more often.

With cache enabled, repeated calls for the same message types reuse those artifacts, which reduces overhead in hot paths.

This behavior is verified in tests:

- same request type reused -> one request wrapper kept (`MediatorFactoryCachingTests.Send_ReusesCachedWrappersAndFactories_WhenCachingEnabled`)
- same notification type reused -> one notification wrapper kept (`MediatorFactoryCachingTests.Publish_ReusesCachedNotificationWrapperAndFactory_WhenCachingEnabled`)
- cache disabled -> wrappers/factories are not retained (`MediatorFactoryCachingTests.Send_DoesNotRetainWrappers_WhenCachingDisabled`)

## Default values

- `EnableWrapperCaching = true`
- `MaxRequestWrappers = 2048`
- `MaxStreamRequestWrappers = 1024`
- `MaxNotificationWrappers = 2048`
- `MaxWrapperFactories = 2048`
- `WrapperCacheSlidingExpiration = null` (disabled)
- `WrapperCacheAbsoluteExpiration = null` (disabled)

## Option-by-option guide

### `EnableWrapperCaching`

- **What it is**: global on/off switch for wrapper and wrapper-factory caching.
- **What it implies**:
  - `true`: Mediora reuses cached runtime artifacts.
  - `false`: Mediora recreates runtime artifacts as needed and does not retain them.
- **When to use**:
  - keep `true` in normal production scenarios.
  - set `false` only for very specific diagnostics or controlled experiments.

### `MaxRequestWrappers`

- **What it is**: max cached wrappers for request/response dispatch (`Send`).
- **What it implies**: bounds memory used by request wrapper entries.
- **When to tune**:
  - raise if your app uses many distinct request types and you see wrapper churn.
  - lower if message type variety is small and you want stricter memory limits.

### `MaxStreamRequestWrappers`

- **What it is**: max cached wrappers for stream request dispatch (`CreateStream`).
- **What it implies**: bounds memory used by stream wrapper entries.
- **When to tune**: same logic as `MaxRequestWrappers`, but for stream requests.

### `MaxNotificationWrappers`

- **What it is**: max cached wrappers for notifications (`Publish`).
- **What it implies**: bounds memory used by notification wrapper entries.
- **When to tune**:
  - raise for systems with many notification types.
  - lower for simpler domains with tighter memory budgets.

### `MaxWrapperFactories`

- **What it is**: max cached compiled factory delegates used to create wrappers.
- **What it implies**: global cap for cached wrapper factories across request, stream, and notification wrappers.
- **When to tune**:
  - increase if many wrapper types are used repeatedly.
  - decrease to reduce retained factory entries.

### `WrapperCacheSlidingExpiration`

- **What it is**: optional idle timeout.
- **What it implies**: an entry expires if it is not accessed for the configured duration.
- **When to use**:
  - useful when traffic is bursty and you want old idle entries removed.

Test-backed behavior:

- entries expire after idle time and are recreated on later access (`Send_CacheSlidingExpiration_ExpiresIdleRequestWrapper`, plus stream/notification equivalents).

### `WrapperCacheAbsoluteExpiration`

- **What it is**: optional max lifetime from creation time.
- **What it implies**: an entry expires after fixed age, even if it is still being accessed.
- **When to use**:
  - useful when you want periodic refresh of cached runtime artifacts.

Test-backed behavior:

- entry can still expire even with intermediate accesses (`Send_CacheAbsoluteExpiration_ExpiresRequestWrapperEvenWithAccess`).

## How bounds and expiration work together

- Entries are always bounded by the configured `Max*` limits.
- If expiration is configured, an entry is removed when any active expiration policy is met.
- In practice: cache size stays bounded and old entries can be rotated out over time.

## Sizing matrix

The default values are intentionally generous for production workloads.

If you reduce them, memory usage goes down, but cache churn can go up (more wrapper/factory recreations).

| Scenario | MaxRequestWrappers | MaxStreamRequestWrappers | MaxNotificationWrappers | MaxWrapperFactories | SlidingExpiration | AbsoluteExpiration |
| --- | ---: | ---: | ---: | ---: | --- | --- |
| Small app | 128 | 64 | 128 | 128 | 30m | 4h |
| Medium app | 512 | 256 | 512 | 512 | 30-60m | 6-12h |
| Large app | 2048 | 1024 | 2048 | 2048 | null or 60m | null or 24h |
| Memory constrained | 256 | 128 | 256 | 256 | 10-20m | 2-4h |

Notes:

- Start with `Medium app` if you do not know your workload yet.
- In high-throughput systems with many message types, prefer `Large app` defaults.
- In tight-memory environments, use `Memory constrained` and monitor latency.

## Practical rule

Use this loop when tuning cache values:

1. Start from defaults or `Medium app` matrix values.
2. Run a realistic load test.
3. If memory is high, reduce `Max*` values gradually.
4. If latency/CPU worsens, increase first `MaxWrapperFactories`, then the wrapper limits that churn most.
5. Keep `EnableWrapperCaching = true` unless you are diagnosing behavior.

## Recommended starter configuration

```csharp
services.AddMediora(options =>
{
    options.RegisterServicesFromAssembly(typeof(Program).Assembly);

    // Start with defaults unless you have measured reasons to tune.
    options.EnableWrapperCaching = true;

    options.MaxRequestWrappers = 2048;
    options.MaxStreamRequestWrappers = 1024;
    options.MaxNotificationWrappers = 2048;
    options.MaxWrapperFactories = 2048;

    // Keep expirations disabled initially.
    options.WrapperCacheSlidingExpiration = null;
    options.WrapperCacheAbsoluteExpiration = null;
});
```

## Example with expirations

```csharp
services.AddMediora(options =>
{
    options.RegisterServicesFromAssembly(typeof(Program).Assembly);

    options.WrapperCacheSlidingExpiration = TimeSpan.FromMinutes(30);
    options.WrapperCacheAbsoluteExpiration = TimeSpan.FromHours(6);
});
```

## Disable caching example

```csharp
services.AddMediora(options =>
{
    options.EnableWrapperCaching = false;
    options.RegisterServicesFromAssembly(typeof(Program).Assembly);
});
```

## Validation rules

- `MaxRequestWrappers`, `MaxStreamRequestWrappers`, `MaxNotificationWrappers`, and `MaxWrapperFactories` must be greater than `0`.
- `WrapperCacheSlidingExpiration` and `WrapperCacheAbsoluteExpiration`, when set, must be greater than `TimeSpan.Zero`.

Invalid values fail fast during `AddMediora(...)` configuration.

## Related

- [Home](Home.md)
- [Getting Started](Getting-Started.md)
