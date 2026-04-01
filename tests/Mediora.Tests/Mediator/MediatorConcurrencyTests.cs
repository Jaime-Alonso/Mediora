using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;

namespace Mediora.Tests;

public sealed class MediatorConcurrencyTests
{
    [Fact]
    public async Task Send_IsThreadSafe_UnderConcurrentCalls()
    {
        var counter = new SendCounter();
        var services = new ServiceCollection();
        services.AddSingleton(counter);
        services.AddSingleton<IRequestHandler<ConcurrentRequest, int>, ConcurrentRequestHandler>();
        services.AddSingleton<IMediator, Mediator>();

        await using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        var tasks = Enumerable.Range(1, 200)
            .Select(static value => new ConcurrentRequest(value))
            .Select(request => mediator.Send(request))
            .ToArray();

        var results = await Task.WhenAll(tasks);

        Assert.Equal(200, results.Length);
        Assert.Equal(200, counter.Count);
    }

    [Fact]
    public async Task Publish_IsThreadSafe_UnderConcurrentCalls()
    {
        var counter = new PublishCounter();
        var services = new ServiceCollection();
        services.AddSingleton(counter);
        services.AddSingleton<INotificationHandler<ConcurrentNotification>, ConcurrentNotificationHandler>();
        services.AddSingleton<IMediator, Mediator>();

        await using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        var tasks = Enumerable.Range(0, 200)
            .Select(static _ => new ConcurrentNotification())
            .Select(notification => mediator.Publish(notification))
            .ToArray();

        await Task.WhenAll(tasks);

        Assert.Equal(200, counter.Count);
    }

    [Fact]
    public async Task CreateStream_IsThreadSafe_UnderConcurrentCalls()
    {
        var counter = new StreamCounter();
        var services = new ServiceCollection();
        services.AddSingleton(counter);
        services.AddSingleton<IStreamRequestHandler<ConcurrentStreamRequest, int>, ConcurrentStreamRequestHandler>();
        services.AddSingleton<IMediator, Mediator>();

        await using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        var tasks = Enumerable.Range(1, 120)
            .Select(static value => new ConcurrentStreamRequest(value))
            .Select(async request =>
            {
                List<int> values = [];
                await foreach (int item in mediator.CreateStream(request))
                {
                    values.Add(item);
                }

                return values;
            })
            .ToArray();

        List<int>[] results = await Task.WhenAll(tasks);

        Assert.Equal(120, results.Length);
        Assert.All(results, static values => Assert.Single(values));
        Assert.Equal(120, counter.Count);
    }

    [Fact]
    public async Task Send_ConcurrentCalls_WithPipelineBehaviors_PreservesPerRequestIsolation()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IRequestHandler<ConcurrentBehaviorRequest, string>, ConcurrentBehaviorRequestHandler>();
        services.AddSingleton<IPipelineBehavior<ConcurrentBehaviorRequest, string>, ConcurrentPrefixBehavior>();
        services.AddSingleton<IMediator, Mediator>();

        await using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        Task<string>[] tasks = Enumerable.Range(1, 150)
            .Select(static value => new ConcurrentBehaviorRequest(value))
            .Select(request => mediator.Send(request))
            .ToArray();

        string[] results = await Task.WhenAll(tasks);

        Assert.Equal(150, results.Length);
        Assert.Equal(
            Enumerable.Range(1, 150).Select(static value => $"behavior-{value * 10}").OrderBy(static value => value),
            results.OrderBy(static value => value));
    }

    [Fact]
    public async Task Publish_ConcurrentCalls_WithParallelModeAndMaxDegreeTwo_ProcessesAllHandlers()
    {
        var counter = new ParallelPublishCounter();
        var services = new ServiceCollection();
        services.AddSingleton(counter);
        services.AddSingleton<INotificationHandler<ConcurrentParallelNotification>, ConcurrentParallelFirstHandler>();
        services.AddSingleton<INotificationHandler<ConcurrentParallelNotification>, ConcurrentParallelSecondHandler>();
        services.AddSingleton<INotificationHandler<ConcurrentParallelNotification>, ConcurrentParallelThirdHandler>();
        services.AddSingleton(new NotificationPublishOptions(
            NotificationPublishMode.ParallelAggregateAll,
            2,
            []));
        services.AddSingleton<IMediator, Mediator>();

        await using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        Task[] tasks = Enumerable.Range(0, 80)
            .Select(static _ => new ConcurrentParallelNotification())
            .Select(notification => mediator.Publish(notification))
            .ToArray();

        await Task.WhenAll(tasks);

        Assert.Equal(80 * 3, counter.Count);
    }

    private sealed class SendCounter
    {
        public int Count;
    }

    private sealed record ConcurrentRequest(int Value) : IRequest<int>;

    private sealed class ConcurrentRequestHandler : IRequestHandler<ConcurrentRequest, int>
    {
        private readonly SendCounter _counter;

        public ConcurrentRequestHandler(SendCounter counter)
        {
            _counter = counter;
        }

        public Task<int> Handle(ConcurrentRequest request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _counter.Count);
            return Task.FromResult(request.Value * 2);
        }
    }

    private sealed class PublishCounter
    {
        public int Count;
    }

    private sealed record ConcurrentNotification : INotification;

    private sealed class ConcurrentNotificationHandler : INotificationHandler<ConcurrentNotification>
    {
        private readonly PublishCounter _counter;

        public ConcurrentNotificationHandler(PublishCounter counter)
        {
            _counter = counter;
        }

        public Task Handle(ConcurrentNotification notification, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _counter.Count);
            return Task.CompletedTask;
        }
    }

    private sealed class StreamCounter
    {
        public int Count;
    }

    private sealed record ConcurrentStreamRequest(int Value) : IStreamRequest<int>;

    private sealed class ConcurrentStreamRequestHandler : IStreamRequestHandler<ConcurrentStreamRequest, int>
    {
        private readonly StreamCounter _counter;

        public ConcurrentStreamRequestHandler(StreamCounter counter)
        {
            _counter = counter;
        }

        public async IAsyncEnumerable<int> Handle(ConcurrentStreamRequest request, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _counter.Count);
            cancellationToken.ThrowIfCancellationRequested();
            yield return request.Value * 3;
            await Task.Yield();
        }
    }

    private sealed record ConcurrentBehaviorRequest(int Value) : IRequest<string>;

    private sealed class ConcurrentBehaviorRequestHandler : IRequestHandler<ConcurrentBehaviorRequest, string>
    {
        public Task<string> Handle(ConcurrentBehaviorRequest request, CancellationToken cancellationToken)
        {
            return Task.FromResult((request.Value * 10).ToString());
        }
    }

    private sealed class ConcurrentPrefixBehavior : IPipelineBehavior<ConcurrentBehaviorRequest, string>
    {
        public async Task<string> Handle(ConcurrentBehaviorRequest request, RequestHandlerDelegate<string> next, CancellationToken cancellationToken)
        {
            string result = await next();
            return $"behavior-{result}";
        }
    }

    private sealed class ParallelPublishCounter
    {
        public int Count;
    }

    private sealed record ConcurrentParallelNotification : INotification;

    private sealed class ConcurrentParallelFirstHandler : INotificationHandler<ConcurrentParallelNotification>
    {
        private readonly ParallelPublishCounter _counter;

        public ConcurrentParallelFirstHandler(ParallelPublishCounter counter)
        {
            _counter = counter;
        }

        public async Task Handle(ConcurrentParallelNotification notification, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _counter.Count);
            await Task.Delay(5, cancellationToken);
        }
    }

    private sealed class ConcurrentParallelSecondHandler : INotificationHandler<ConcurrentParallelNotification>
    {
        private readonly ParallelPublishCounter _counter;

        public ConcurrentParallelSecondHandler(ParallelPublishCounter counter)
        {
            _counter = counter;
        }

        public async Task Handle(ConcurrentParallelNotification notification, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _counter.Count);
            await Task.Delay(5, cancellationToken);
        }
    }

    private sealed class ConcurrentParallelThirdHandler : INotificationHandler<ConcurrentParallelNotification>
    {
        private readonly ParallelPublishCounter _counter;

        public ConcurrentParallelThirdHandler(ParallelPublishCounter counter)
        {
            _counter = counter;
        }

        public async Task Handle(ConcurrentParallelNotification notification, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _counter.Count);
            await Task.Delay(5, cancellationToken);
        }
    }
}
