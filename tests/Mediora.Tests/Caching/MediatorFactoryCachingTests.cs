using System.Runtime.CompilerServices;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace Mediora.Tests;

public sealed class MediatorFactoryCachingTests
{
    [Fact]
    public async Task Send_ReusesCachedWrappersAndFactories_WhenCachingEnabled()
    {
        var services = new ServiceCollection();
        services.AddMediora(options =>
        {
            options.RegisterServicesFromAssembly(typeof(CachedRequestHandler).Assembly);
            options.MaxRequestWrappers = 8;
            options.MaxWrapperFactories = 8;
        });

        await using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();
        var cacheStore = provider.GetRequiredService<MediatorCacheStore>();

        _ = await mediator.Send(new CachedRequest("one"));
        _ = await mediator.Send(new CachedRequest("two"));

        Assert.Equal(1, cacheStore.RequestWrapperCount);
        Assert.Equal(1, cacheStore.WrapperFactoryCount);
    }

    [Fact]
    public async Task Send_DoesNotRetainWrappers_WhenCachingDisabled()
    {
        var services = new ServiceCollection();
        services.AddMediora(options =>
        {
            options.EnableWrapperCaching = false;
            options.RegisterServicesFromAssembly(typeof(CachedRequestHandler).Assembly);
        });

        await using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();
        var cacheStore = provider.GetRequiredService<MediatorCacheStore>();

        _ = await mediator.Send(new CachedRequest("one"));
        _ = await mediator.Send(new CachedRequest("two"));

        Assert.Equal(0, cacheStore.RequestWrapperCount);
        Assert.Equal(0, cacheStore.WrapperFactoryCount);
    }

    [Fact]
    public async Task CreateStream_ReusesCachedWrappersAndFactories_WhenCachingEnabled()
    {
        var services = new ServiceCollection();
        services.AddMediora(options =>
        {
            options.RegisterServicesFromAssembly(typeof(CachedRequestHandler).Assembly);
            options.MaxStreamRequestWrappers = 8;
            options.MaxWrapperFactories = 8;
        });

        await using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();
        var cacheStore = provider.GetRequiredService<MediatorCacheStore>();

        _ = await CollectAsync(mediator.CreateStream(new CachedStreamRequest("one")));
        _ = await CollectAsync(mediator.CreateStream(new CachedStreamRequest("two")));

        Assert.Equal(1, cacheStore.StreamRequestWrapperCount);
        Assert.Equal(1, cacheStore.WrapperFactoryCount);
    }

    [Fact]
    public async Task Publish_ReusesCachedNotificationWrapperAndFactory_WhenCachingEnabled()
    {
        var services = new ServiceCollection();
        services.AddMediora(options =>
        {
            options.RegisterServicesFromAssembly(typeof(CachedRequestHandler).Assembly);
            options.MaxNotificationWrappers = 8;
            options.MaxWrapperFactories = 8;
        });

        await using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();
        var cacheStore = provider.GetRequiredService<MediatorCacheStore>();

        await mediator.Publish(new CachedNotification("one"));
        await mediator.Publish(new CachedNotification("two"));

        Assert.Equal(1, cacheStore.NotificationWrapperCount);
        Assert.Equal(1, cacheStore.WrapperFactoryCount);
    }

    [Fact]
    public async Task CreateStream_AndPublish_DoNotRetainWrappers_WhenCachingDisabled()
    {
        var services = new ServiceCollection();
        services.AddMediora(options =>
        {
            options.EnableWrapperCaching = false;
            options.RegisterServicesFromAssembly(typeof(CachedRequestHandler).Assembly);
        });

        await using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();
        var cacheStore = provider.GetRequiredService<MediatorCacheStore>();

        _ = await mediator.Send(new CachedRequest("one"));
        _ = await CollectAsync(mediator.CreateStream(new CachedStreamRequest("one")));
        await mediator.Publish(new CachedNotification("one"));

        Assert.Equal(0, cacheStore.RequestWrapperCount);
        Assert.Equal(0, cacheStore.StreamRequestWrapperCount);
        Assert.Equal(0, cacheStore.NotificationWrapperCount);
        Assert.Equal(0, cacheStore.WrapperFactoryCount);
    }

    [Fact]
    public async Task Send_RespectsMaxRequestWrapperCacheSize()
    {
        var services = new ServiceCollection();
        services.AddMediora(options =>
        {
            options.RegisterServicesFromAssembly(typeof(CachedRequestHandler).Assembly);
            options.MaxRequestWrappers = 2;
            options.MaxWrapperFactories = 16;
        });

        await using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();
        var cacheStore = provider.GetRequiredService<MediatorCacheStore>();

        _ = await mediator.Send(new CachedRequestOne());
        _ = await mediator.Send(new CachedRequestTwo());
        _ = await mediator.Send(new CachedRequestThree());

        Assert.True(cacheStore.RequestWrapperCount <= 2);
    }

    [Fact]
    public async Task CreateStream_RespectsMaxStreamRequestWrapperCacheSize()
    {
        var services = new ServiceCollection();
        services.AddMediora(options =>
        {
            options.RegisterServicesFromAssembly(typeof(CachedRequestHandler).Assembly);
            options.MaxStreamRequestWrappers = 2;
            options.MaxWrapperFactories = 16;
        });

        await using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();
        var cacheStore = provider.GetRequiredService<MediatorCacheStore>();

        _ = await CollectAsync(mediator.CreateStream(new CachedStreamOne()));
        _ = await CollectAsync(mediator.CreateStream(new CachedStreamTwo()));
        _ = await CollectAsync(mediator.CreateStream(new CachedStreamThree()));

        Assert.True(cacheStore.StreamRequestWrapperCount <= 2);
    }

    [Fact]
    public async Task Publish_RespectsMaxNotificationWrapperCacheSize()
    {
        var services = new ServiceCollection();
        services.AddMediora(options =>
        {
            options.RegisterServicesFromAssembly(typeof(CachedRequestHandler).Assembly);
            options.MaxNotificationWrappers = 2;
            options.MaxWrapperFactories = 16;
        });

        await using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();
        var cacheStore = provider.GetRequiredService<MediatorCacheStore>();

        await mediator.Publish(new CachedNotificationOne());
        await mediator.Publish(new CachedNotificationTwo());
        await mediator.Publish(new CachedNotificationThree());

        Assert.True(cacheStore.NotificationWrapperCount <= 2);
    }

    [Fact]
    public async Task WrapperFactoryCache_RespectsMaxFactorySize_AcrossRequestStreamAndNotification()
    {
        var services = new ServiceCollection();
        services.AddMediora(options =>
        {
            options.RegisterServicesFromAssembly(typeof(CachedRequestHandler).Assembly);
            options.MaxRequestWrappers = 16;
            options.MaxStreamRequestWrappers = 16;
            options.MaxNotificationWrappers = 16;
            options.MaxWrapperFactories = 2;
        });

        await using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();
        var cacheStore = provider.GetRequiredService<MediatorCacheStore>();

        _ = await mediator.Send(new CachedRequest("one"));
        _ = await CollectAsync(mediator.CreateStream(new CachedStreamRequest("one")));
        await mediator.Publish(new CachedNotification("one"));

        Assert.True(cacheStore.WrapperFactoryCount <= 2);
    }

    [Fact]
    public async Task Send_CacheSlidingExpiration_ExpiresIdleRequestWrapper()
    {
        var services = new ServiceCollection();
        services.AddMediora(options =>
        {
            options.RegisterServicesFromAssembly(typeof(CachedRequestHandler).Assembly);
            options.WrapperCacheSlidingExpiration = TimeSpan.FromMilliseconds(120);
            options.WrapperCacheAbsoluteExpiration = null;
            options.MaxRequestWrappers = 8;
            options.MaxWrapperFactories = 8;
        });

        await using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();
        var cacheStore = provider.GetRequiredService<MediatorCacheStore>();

        _ = await mediator.Send(new CachedRequest("one"));
        object? firstWrapper = GetRequestWrapperEntry(cacheStore, typeof(CachedRequest), typeof(string));

        await Task.Delay(220);

        _ = await mediator.Send(new CachedRequest("two"));
        object? secondWrapper = GetRequestWrapperEntry(cacheStore, typeof(CachedRequest), typeof(string));

        Assert.NotNull(firstWrapper);
        Assert.NotNull(secondWrapper);
        Assert.NotSame(firstWrapper, secondWrapper);
    }

    [Fact]
    public async Task CreateStream_CacheSlidingExpiration_ExpiresIdleStreamWrapper()
    {
        var services = new ServiceCollection();
        services.AddMediora(options =>
        {
            options.RegisterServicesFromAssembly(typeof(CachedRequestHandler).Assembly);
            options.WrapperCacheSlidingExpiration = TimeSpan.FromMilliseconds(120);
            options.WrapperCacheAbsoluteExpiration = null;
            options.MaxStreamRequestWrappers = 8;
            options.MaxWrapperFactories = 8;
        });

        await using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();
        var cacheStore = provider.GetRequiredService<MediatorCacheStore>();

        _ = await CollectAsync(mediator.CreateStream(new CachedStreamRequest("one")));
        object? firstWrapper = GetStreamWrapperEntry(cacheStore, typeof(CachedStreamRequest), typeof(string));

        await Task.Delay(220);

        _ = await CollectAsync(mediator.CreateStream(new CachedStreamRequest("two")));
        object? secondWrapper = GetStreamWrapperEntry(cacheStore, typeof(CachedStreamRequest), typeof(string));

        Assert.NotNull(firstWrapper);
        Assert.NotNull(secondWrapper);
        Assert.NotSame(firstWrapper, secondWrapper);
    }

    [Fact]
    public async Task Publish_CacheSlidingExpiration_ExpiresIdleNotificationWrapper()
    {
        var services = new ServiceCollection();
        services.AddMediora(options =>
        {
            options.RegisterServicesFromAssembly(typeof(CachedRequestHandler).Assembly);
            options.WrapperCacheSlidingExpiration = TimeSpan.FromMilliseconds(120);
            options.WrapperCacheAbsoluteExpiration = null;
            options.MaxNotificationWrappers = 8;
            options.MaxWrapperFactories = 8;
        });

        await using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();
        var cacheStore = provider.GetRequiredService<MediatorCacheStore>();

        await mediator.Publish(new CachedNotification("one"));
        object? firstWrapper = GetNotificationWrapperEntry(cacheStore, typeof(CachedNotification));

        await Task.Delay(220);

        await mediator.Publish(new CachedNotification("two"));
        object? secondWrapper = GetNotificationWrapperEntry(cacheStore, typeof(CachedNotification));

        Assert.NotNull(firstWrapper);
        Assert.NotNull(secondWrapper);
        Assert.NotSame(firstWrapper, secondWrapper);
    }

    [Fact]
    public async Task Send_CacheAbsoluteExpiration_ExpiresRequestWrapperEvenWithAccess()
    {
        var services = new ServiceCollection();
        services.AddMediora(options =>
        {
            options.RegisterServicesFromAssembly(typeof(CachedRequestHandler).Assembly);
            options.WrapperCacheSlidingExpiration = null;
            options.WrapperCacheAbsoluteExpiration = TimeSpan.FromMilliseconds(150);
            options.MaxRequestWrappers = 8;
            options.MaxWrapperFactories = 8;
        });

        await using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();
        var cacheStore = provider.GetRequiredService<MediatorCacheStore>();

        _ = await mediator.Send(new CachedRequest("one"));
        object? firstWrapper = GetRequestWrapperEntry(cacheStore, typeof(CachedRequest), typeof(string));

        await Task.Delay(25);
        _ = await mediator.Send(new CachedRequest("two"));
        object? middleWrapper = GetRequestWrapperEntry(cacheStore, typeof(CachedRequest), typeof(string));

        await Task.Delay(170);
        _ = await mediator.Send(new CachedRequest("three"));
        object? lastWrapper = GetRequestWrapperEntry(cacheStore, typeof(CachedRequest), typeof(string));

        Assert.NotNull(firstWrapper);
        Assert.NotNull(middleWrapper);
        Assert.NotNull(lastWrapper);
        Assert.Same(firstWrapper, middleWrapper);
        Assert.NotSame(middleWrapper, lastWrapper);
    }

    [Fact]
    public async Task WrapperFactoryCache_AbsoluteExpiration_ExpiresFactoryEntries()
    {
        var services = new ServiceCollection();
        services.AddMediora(options =>
        {
            options.RegisterServicesFromAssembly(typeof(CachedRequestHandler).Assembly);
            options.WrapperCacheSlidingExpiration = null;
            options.WrapperCacheAbsoluteExpiration = TimeSpan.FromMilliseconds(150);
            options.MaxRequestWrappers = 8;
            options.MaxWrapperFactories = 8;
        });

        await using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();
        var cacheStore = provider.GetRequiredService<MediatorCacheStore>();
        Type wrapperType = typeof(Mediora.Internal.RequestHandlerWrapper<CachedRequest, string>);

        _ = await mediator.Send(new CachedRequest("one"));
        Delegate? firstFactory = GetWrapperFactoryEntry(cacheStore, wrapperType);

        await Task.Delay(220);

        _ = await mediator.Send(new CachedRequest("two"));
        Delegate? secondFactory = GetWrapperFactoryEntry(cacheStore, wrapperType);

        Assert.NotNull(firstFactory);
        Assert.NotNull(secondFactory);
        Assert.NotSame(firstFactory, secondFactory);
    }

    [Fact]
    public async Task Publish_Stress_ShortRun_DoesNotGrowWrapperCachesUnbounded()
    {
        var services = new ServiceCollection();
        services.AddMediora(options =>
        {
            options.RegisterServicesFromAssembly(typeof(CachedRequestHandler).Assembly);
            options.MaxRequestWrappers = 3;
            options.MaxStreamRequestWrappers = 3;
            options.MaxNotificationWrappers = 3;
            options.MaxWrapperFactories = 3;
        });

        await using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();
        var cacheStore = provider.GetRequiredService<MediatorCacheStore>();

        for (int i = 0; i < 80; i++)
        {
            _ = await mediator.Send(new CachedRequestOne());
            _ = await mediator.Send(new CachedRequestTwo());
            _ = await mediator.Send(new CachedRequestThree());

            _ = await CollectAsync(mediator.CreateStream(new CachedStreamOne()));
            _ = await CollectAsync(mediator.CreateStream(new CachedStreamTwo()));
            _ = await CollectAsync(mediator.CreateStream(new CachedStreamThree()));

            await mediator.Publish(new CachedNotificationOne());
            await mediator.Publish(new CachedNotificationTwo());
            await mediator.Publish(new CachedNotificationThree());
        }

        Assert.True(cacheStore.RequestWrapperCount <= 3);
        Assert.True(cacheStore.StreamRequestWrapperCount <= 3);
        Assert.True(cacheStore.NotificationWrapperCount <= 3);
        Assert.True(cacheStore.WrapperFactoryCount <= 3);
    }

    private static async Task<List<T>> CollectAsync<T>(IAsyncEnumerable<T> source)
    {
        List<T> items = [];

        await foreach (T item in source)
        {
            items.Add(item);
        }

        return items;
    }

    private static object? GetRequestWrapperEntry(MediatorCacheStore cacheStore, Type requestType, Type responseType)
        => GetCacheEntryValue(cacheStore, "_requestWrappers", (requestType, responseType));

    private static object? GetStreamWrapperEntry(MediatorCacheStore cacheStore, Type requestType, Type responseType)
        => GetCacheEntryValue(cacheStore, "_streamRequestWrappers", (requestType, responseType));

    private static object? GetNotificationWrapperEntry(MediatorCacheStore cacheStore, Type notificationType)
        => GetCacheEntryValue(cacheStore, "_notificationWrappers", notificationType);

    private static Delegate? GetWrapperFactoryEntry(MediatorCacheStore cacheStore, Type wrapperType)
        => GetCacheEntryValue(cacheStore, "_wrapperFactories", wrapperType) as Delegate;

    private static object? GetCacheEntryValue(MediatorCacheStore cacheStore, string cacheFieldName, object key)
    {
        FieldInfo cacheField = typeof(MediatorCacheStore).GetField(cacheFieldName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Missing cache field '{cacheFieldName}'.");

        object cache = cacheField.GetValue(cacheStore)
            ?? throw new InvalidOperationException($"Cache field '{cacheFieldName}' is null.");

        FieldInfo entriesField = cache.GetType().GetField("_entries", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Missing entries field for '{cacheFieldName}'.");

        object entries = entriesField.GetValue(cache)
            ?? throw new InvalidOperationException($"Entries for '{cacheFieldName}' are null.");

        MethodInfo tryGetValue = entries.GetType().GetMethod("TryGetValue")
            ?? throw new InvalidOperationException($"Missing TryGetValue for '{cacheFieldName}'.");

        object?[] parameters = [key, null];
        bool found = (bool)(tryGetValue.Invoke(entries, parameters)
            ?? throw new InvalidOperationException($"TryGetValue invocation failed for '{cacheFieldName}'."));

        if (!found || parameters[1] is null)
        {
            return null;
        }

        PropertyInfo valueProperty = parameters[1]!.GetType().GetProperty("Value", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Missing Value property for '{cacheFieldName}'.");

        return valueProperty.GetValue(parameters[1]);
    }

    private sealed record CachedRequest(string Value) : IRequest<string>;

    private sealed class CachedRequestHandler : IRequestHandler<CachedRequest, string>
    {
        public Task<string> Handle(CachedRequest request, CancellationToken cancellationToken)
        {
            return Task.FromResult(request.Value.ToUpperInvariant());
        }
    }

    private sealed record CachedStreamRequest(string Value) : IStreamRequest<string>;

    private sealed class CachedStreamRequestHandler : IStreamRequestHandler<CachedStreamRequest, string>
    {
        public async IAsyncEnumerable<string> Handle(CachedStreamRequest request, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return request.Value.ToUpperInvariant();
            await Task.Yield();
        }
    }

    private sealed record CachedNotification(string Value) : INotification;

    private sealed class CachedNotificationHandler : INotificationHandler<CachedNotification>
    {
        public Task Handle(CachedNotification notification, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    private sealed record CachedRequestOne : IRequest<string>;
    private sealed record CachedRequestTwo : IRequest<string>;
    private sealed record CachedRequestThree : IRequest<string>;

    private sealed class CachedRequestOneHandler : IRequestHandler<CachedRequestOne, string>
    {
        public Task<string> Handle(CachedRequestOne request, CancellationToken cancellationToken) => Task.FromResult("one");
    }

    private sealed class CachedRequestTwoHandler : IRequestHandler<CachedRequestTwo, string>
    {
        public Task<string> Handle(CachedRequestTwo request, CancellationToken cancellationToken) => Task.FromResult("two");
    }

    private sealed class CachedRequestThreeHandler : IRequestHandler<CachedRequestThree, string>
    {
        public Task<string> Handle(CachedRequestThree request, CancellationToken cancellationToken) => Task.FromResult("three");
    }

    private sealed record CachedStreamOne : IStreamRequest<string>;
    private sealed record CachedStreamTwo : IStreamRequest<string>;
    private sealed record CachedStreamThree : IStreamRequest<string>;

    private sealed class CachedStreamOneHandler : IStreamRequestHandler<CachedStreamOne, string>
    {
        public async IAsyncEnumerable<string> Handle(CachedStreamOne request, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return "one";
            await Task.Yield();
        }
    }

    private sealed class CachedStreamTwoHandler : IStreamRequestHandler<CachedStreamTwo, string>
    {
        public async IAsyncEnumerable<string> Handle(CachedStreamTwo request, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return "two";
            await Task.Yield();
        }
    }

    private sealed class CachedStreamThreeHandler : IStreamRequestHandler<CachedStreamThree, string>
    {
        public async IAsyncEnumerable<string> Handle(CachedStreamThree request, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return "three";
            await Task.Yield();
        }
    }

    private sealed record CachedNotificationOne : INotification;
    private sealed record CachedNotificationTwo : INotification;
    private sealed record CachedNotificationThree : INotification;

    private sealed class CachedNotificationOneHandler : INotificationHandler<CachedNotificationOne>
    {
        public Task Handle(CachedNotificationOne notification, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class CachedNotificationTwoHandler : INotificationHandler<CachedNotificationTwo>
    {
        public Task Handle(CachedNotificationTwo notification, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class CachedNotificationThreeHandler : INotificationHandler<CachedNotificationThree>
    {
        public Task Handle(CachedNotificationThree notification, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
