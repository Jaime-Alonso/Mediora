using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;

namespace Mediora.Tests;

public sealed class MediatorStreamTests
{
    [Fact]
    public async Task CreateStream_ResolvesAndInvokesCorrectHandler()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IStreamRequestHandler<NumbersStreamRequest, int>, NumbersStreamHandler>();
        services.AddSingleton<IMediator, Mediator>();

        await using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        var values = await CollectAsync(mediator.CreateStream(new NumbersStreamRequest(3)));

        Assert.Equal([1, 2, 3], values);
    }

    [Fact]
    public void CreateStream_ThrowsArgumentNullException_WhenRequestIsNull()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IMediator, Mediator>();

        using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        var exception = Assert.Throws<ArgumentNullException>(() => mediator.CreateStream<string>(null!));

        Assert.Equal("request", exception.ParamName);
    }

    [Fact]
    public async Task CreateStream_ThrowsInvalidOperationException_WhenNoHandlerIsRegistered()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IMediator, Mediator>();

        await using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await CollectAsync(mediator.CreateStream(new UnhandledStreamRequest())));

        Assert.Contains("StreamRequestHandlerWrapper: No handler registered", exception.Message);
    }

    [Fact]
    public async Task CreateStream_PassesCancellationTokenToHandler()
    {
        var tokenStore = new StreamTokenStore();
        var services = new ServiceCollection();
        services.AddSingleton(tokenStore);
        services.AddSingleton<IStreamRequestHandler<TokenStreamRequest, string>, TokenStreamHandler>();
        services.AddSingleton<IMediator, Mediator>();

        await using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();
        using var cts = new CancellationTokenSource();

        _ = await CollectAsync(mediator.CreateStream(new TokenStreamRequest(), cts.Token), cts.Token);

        Assert.Equal(cts.Token, tokenStore.LastToken);
    }

    [Fact]
    public async Task CreateStream_PropagatesHandlerExceptionDuringEnumeration()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IStreamRequestHandler<ThrowingStreamRequest, int>, ThrowingStreamHandler>();
        services.AddSingleton<IMediator, Mediator>();

        await using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await CollectAsync(mediator.CreateStream(new ThrowingStreamRequest())));

        Assert.Equal("stream-failure", exception.Message);
    }

    [Fact]
    public async Task CreateStream_BehaviorWrapsHandlerExecution()
    {
        var trace = new List<string>();
        var services = new ServiceCollection();
        services.AddSingleton(trace);
        services.AddSingleton<IStreamRequestHandler<TraceStreamRequest, string>, TraceStreamHandler>();
        services.AddSingleton<IStreamPipelineBehavior<TraceStreamRequest, string>, TraceStreamBehavior>();
        services.AddSingleton<IMediator, Mediator>();

        await using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        var values = await CollectAsync(mediator.CreateStream(new TraceStreamRequest()));

        Assert.Equal(["value"], values);
        Assert.Equal(["before", "handler", "after"], trace);
    }

    [Fact]
    public async Task CreateStream_MultipleBehaviorsExecuteInRegistrationOrder()
    {
        var trace = new List<string>();
        var services = new ServiceCollection();
        services.AddSingleton(trace);
        services.AddSingleton<IStreamRequestHandler<OrderStreamRequest, string>, OrderStreamHandler>();
        services.AddSingleton<IStreamPipelineBehavior<OrderStreamRequest, string>, OuterStreamBehavior>();
        services.AddSingleton<IStreamPipelineBehavior<OrderStreamRequest, string>, InnerStreamBehavior>();
        services.AddSingleton<IMediator, Mediator>();

        await using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        var values = await CollectAsync(mediator.CreateStream(new OrderStreamRequest()));

        Assert.Equal(["ok"], values);
        Assert.Equal(["outer-before", "inner-before", "handler", "inner-after", "outer-after"], trace);
    }

    [Fact]
    public async Task CreateStream_BehaviorCanShortCircuitPipeline()
    {
        var trace = new List<string>();
        var services = new ServiceCollection();
        services.AddSingleton(trace);
        services.AddSingleton<IStreamRequestHandler<ShortCircuitStreamRequest, string>, ShortCircuitStreamHandler>();
        services.AddSingleton<IStreamPipelineBehavior<ShortCircuitStreamRequest, string>, ShortCircuitStreamBehavior>();
        services.AddSingleton<IMediator, Mediator>();

        await using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        var values = await CollectAsync(mediator.CreateStream(new ShortCircuitStreamRequest()));

        Assert.Equal(["short-circuited"], values);
        Assert.Equal(["behavior"], trace);
    }

    [Fact]
    public async Task CreateStream_MultipleBehaviors_WithShortCircuit_SkipsInnerAndHandler()
    {
        var trace = new List<string>();
        var services = new ServiceCollection();
        services.AddSingleton(trace);
        services.AddSingleton<IStreamRequestHandler<MultiShortCircuitStreamRequest, string>, MultiShortCircuitStreamHandler>();
        services.AddSingleton<IStreamPipelineBehavior<MultiShortCircuitStreamRequest, string>, OuterMultiShortCircuitStreamBehavior>();
        services.AddSingleton<IStreamPipelineBehavior<MultiShortCircuitStreamRequest, string>, InnerMultiShortCircuitStreamBehavior>();
        services.AddSingleton<IMediator, Mediator>();

        await using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        var values = await CollectAsync(mediator.CreateStream(new MultiShortCircuitStreamRequest()));

        Assert.Equal(["inner-short-circuited"], values);
        Assert.Equal(["outer-before", "inner-short", "outer-after"], trace);
    }

    [Fact]
    public async Task CreateStream_BehaviorReceivesCancellation_AndStopsBeforeNext()
    {
        var counter = new StreamCallCounter();
        var services = new ServiceCollection();
        services.AddSingleton(counter);
        services.AddSingleton<IStreamRequestHandler<CancellableBehaviorStreamRequest, string>, CancellableBehaviorStreamHandler>();
        services.AddSingleton<IStreamPipelineBehavior<CancellableBehaviorStreamRequest, string>, CancellationAwareStreamBehavior>();
        services.AddSingleton<IMediator, Mediator>();

        await using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await CollectAsync(mediator.CreateStream(new CancellableBehaviorStreamRequest(), cancellation.Token), cancellation.Token));
        Assert.Equal(0, counter.Count);
    }

    [Fact]
    public async Task CreateStream_ConcurrentCalls_WithStreamBehaviors_PreservesIsolation()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IStreamRequestHandler<ConcurrentBehaviorStreamRequest, int>, ConcurrentBehaviorStreamHandler>();
        services.AddSingleton<IStreamPipelineBehavior<ConcurrentBehaviorStreamRequest, int>, ConcurrentPassThroughStreamBehavior>();
        services.AddSingleton<IMediator, Mediator>();

        await using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        Task<int>[] tasks = Enumerable.Range(1, 120)
            .Select(async value =>
            {
                List<int> values = await CollectAsync(mediator.CreateStream(new ConcurrentBehaviorStreamRequest(value)));
                Assert.Single(values);
                return values[0];
            })
            .ToArray();

        int[] results = await Task.WhenAll(tasks);

        Assert.Equal(120, results.Length);
        Assert.Equal(Enumerable.Range(1, 120).OrderBy(static value => value), results.OrderBy(static value => value));
    }

    [Fact]
    public async Task CreateStream_CancellationDuringEnumeration_StopsPipelineAndHandler()
    {
        var trace = new List<string>();
        var services = new ServiceCollection();
        services.AddSingleton(trace);
        services.AddSingleton<IStreamRequestHandler<CancellableDuringEnumerationRequest, string>, CancellableDuringEnumerationHandler>();
        services.AddSingleton<IStreamPipelineBehavior<CancellableDuringEnumerationRequest, string>, CancellableDuringEnumerationBehavior>();
        services.AddSingleton<IMediator, Mediator>();

        await using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();
        using var cancellation = new CancellationTokenSource();

        await using var enumerator = mediator.CreateStream(new CancellableDuringEnumerationRequest(), cancellation.Token)
            .GetAsyncEnumerator(cancellation.Token);

        Assert.True(await enumerator.MoveNextAsync());
        Assert.Equal("first", enumerator.Current);

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await enumerator.MoveNextAsync().AsTask());
        Assert.Equal(["behavior-before", "handler-yield-1", "behavior-yield", "handler-cancelled"], trace);
    }

    [Fact]
    public async Task AddMediora_ResolvesAndExecutesOpenGenericStreamPipelineBehavior()
    {
        var services = new ServiceCollection();
        services.AddMediora(options =>
        {
            options.RegisterServicesFromAssembly(typeof(OpenGenericStreamRequestHandler).Assembly);
            options.AddOpenStreamBehavior(typeof(OpenGenericStreamBehavior<,>));
        });

        await using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();
        OpenGenericStreamRecorder.Clear();

        var values = await CollectAsync(mediator.CreateStream(new OpenGenericStreamRequest()));

        Assert.Equal(["ok"], values);
        Assert.Equal(["before", "handler", "after"], OpenGenericStreamRecorder.GetSteps());
    }

    [Fact]
    public async Task AddMediora_MultipleConfiguredStreamBehaviors_ExecuteInConfigurationOrder()
    {
        var services = new ServiceCollection();
        services.AddMediora(options =>
        {
            options.RegisterServicesFromAssembly(typeof(OpenGenericStreamRequestHandler).Assembly);
            options.AddOpenStreamBehavior(typeof(ConfiguredFirstStreamBehavior<,>));
            options.AddOpenStreamBehavior(typeof(ConfiguredSecondStreamBehavior<,>));
        });

        await using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();
        ConfiguredStreamBehaviorRecorder.Clear();

        var values = await CollectAsync(mediator.CreateStream(new ConfiguredOrderStreamRequest()));

        Assert.Equal(["ok"], values);
        Assert.Equal(["first-before", "second-before", "handler", "second-after", "first-after"], ConfiguredStreamBehaviorRecorder.GetSteps());
    }

    [Fact]
    public async Task AddMediora_ConfiguredClosedAndOpenStreamBehaviors_ExecuteInConfigurationOrder()
    {
        var services = new ServiceCollection();
        services.AddMediora(options =>
        {
            options.RegisterServicesFromAssembly(typeof(OpenGenericStreamRequestHandler).Assembly);
            options.AddStreamBehavior<ConfiguredClosedStreamBehavior>();
            options.AddOpenStreamBehavior(typeof(ConfiguredSecondStreamBehavior<,>));
        });

        await using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();
        ConfiguredStreamBehaviorRecorder.Clear();

        var values = await CollectAsync(mediator.CreateStream(new ConfiguredOrderStreamRequest()));

        Assert.Equal(["ok"], values);
        Assert.Equal(["closed-before", "second-before", "handler", "second-after", "closed-after"], ConfiguredStreamBehaviorRecorder.GetSteps());
    }

    [Fact]
    public async Task AddMediora_ConfiguredOpenThenClosedStreamBehaviors_ExecuteInConfigurationOrder()
    {
        var services = new ServiceCollection();
        services.AddMediora(options =>
        {
            options.RegisterServicesFromAssembly(typeof(OpenGenericStreamRequestHandler).Assembly);
            options.AddOpenStreamBehavior(typeof(ConfiguredFirstStreamBehavior<,>));
            options.AddStreamBehavior<ConfiguredClosedStreamBehavior>();
        });

        await using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();
        ConfiguredStreamBehaviorRecorder.Clear();

        var values = await CollectAsync(mediator.CreateStream(new ConfiguredOrderStreamRequest()));

        Assert.Equal(["ok"], values);
        Assert.Equal(["first-before", "closed-before", "handler", "closed-after", "first-after"], ConfiguredStreamBehaviorRecorder.GetSteps());
    }

    private static async Task<List<T>> CollectAsync<T>(IAsyncEnumerable<T> source, CancellationToken cancellationToken = default)
    {
        List<T> items = [];

        await foreach (var item in source.WithCancellation(cancellationToken))
        {
            items.Add(item);
        }

        return items;
    }

    private sealed record NumbersStreamRequest(int Count) : IStreamRequest<int>;

    private sealed class NumbersStreamHandler : IStreamRequestHandler<NumbersStreamRequest, int>
    {
        public async IAsyncEnumerable<int> Handle(NumbersStreamRequest request, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            for (var i = 1; i <= request.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return i;
                await Task.Yield();
            }
        }
    }

    private sealed record UnhandledStreamRequest : IStreamRequest<string>;

    private sealed record TokenStreamRequest : IStreamRequest<string>;

    private sealed class StreamTokenStore
    {
        public CancellationToken LastToken { get; set; }
    }

    private sealed class TokenStreamHandler : IStreamRequestHandler<TokenStreamRequest, string>
    {
        private readonly StreamTokenStore _tokenStore;

        public TokenStreamHandler(StreamTokenStore tokenStore)
        {
            _tokenStore = tokenStore;
        }

        public async IAsyncEnumerable<string> Handle(TokenStreamRequest request, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            _tokenStore.LastToken = cancellationToken;
            cancellationToken.ThrowIfCancellationRequested();
            yield return "ok";
            await Task.Yield();
        }
    }

    private sealed record ThrowingStreamRequest : IStreamRequest<int>;

    private sealed class ThrowingStreamHandler : IStreamRequestHandler<ThrowingStreamRequest, int>
    {
        public async IAsyncEnumerable<int> Handle(ThrowingStreamRequest request, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return 1;
            await Task.Yield();
            throw new InvalidOperationException("stream-failure");
        }
    }

    private sealed record TraceStreamRequest : IStreamRequest<string>;

    private sealed class TraceStreamHandler : IStreamRequestHandler<TraceStreamRequest, string>
    {
        private readonly List<string> _trace;

        public TraceStreamHandler(List<string> trace)
        {
            _trace = trace;
        }

        public async IAsyncEnumerable<string> Handle(TraceStreamRequest request, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            _trace.Add("handler");
            yield return "value";
            await Task.Yield();
        }
    }

    private sealed class TraceStreamBehavior : IStreamPipelineBehavior<TraceStreamRequest, string>
    {
        private readonly List<string> _trace;

        public TraceStreamBehavior(List<string> trace)
        {
            _trace = trace;
        }

        public async IAsyncEnumerable<string> Handle(TraceStreamRequest request, StreamHandlerDelegate<string> next, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            _trace.Add("before");

            await foreach (var item in next().WithCancellation(cancellationToken))
            {
                yield return item;
            }

            _trace.Add("after");
        }
    }

    private sealed record OrderStreamRequest : IStreamRequest<string>;

    private sealed class OrderStreamHandler : IStreamRequestHandler<OrderStreamRequest, string>
    {
        private readonly List<string> _trace;

        public OrderStreamHandler(List<string> trace)
        {
            _trace = trace;
        }

        public async IAsyncEnumerable<string> Handle(OrderStreamRequest request, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            _trace.Add("handler");
            yield return "ok";
            await Task.Yield();
        }
    }

    private sealed class OuterStreamBehavior : IStreamPipelineBehavior<OrderStreamRequest, string>
    {
        private readonly List<string> _trace;

        public OuterStreamBehavior(List<string> trace)
        {
            _trace = trace;
        }

        public async IAsyncEnumerable<string> Handle(OrderStreamRequest request, StreamHandlerDelegate<string> next, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            _trace.Add("outer-before");

            await foreach (var item in next().WithCancellation(cancellationToken))
            {
                yield return item;
            }

            _trace.Add("outer-after");
        }
    }

    private sealed class InnerStreamBehavior : IStreamPipelineBehavior<OrderStreamRequest, string>
    {
        private readonly List<string> _trace;

        public InnerStreamBehavior(List<string> trace)
        {
            _trace = trace;
        }

        public async IAsyncEnumerable<string> Handle(OrderStreamRequest request, StreamHandlerDelegate<string> next, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            _trace.Add("inner-before");

            await foreach (var item in next().WithCancellation(cancellationToken))
            {
                yield return item;
            }

            _trace.Add("inner-after");
        }
    }

    private sealed record ShortCircuitStreamRequest : IStreamRequest<string>;

    private sealed class ShortCircuitStreamHandler : IStreamRequestHandler<ShortCircuitStreamRequest, string>
    {
        private readonly List<string> _trace;

        public ShortCircuitStreamHandler(List<string> trace)
        {
            _trace = trace;
        }

        public async IAsyncEnumerable<string> Handle(ShortCircuitStreamRequest request, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            _trace.Add("handler");
            cancellationToken.ThrowIfCancellationRequested();
            yield return "handler-value";
            await Task.Yield();
        }
    }

    private sealed class ShortCircuitStreamBehavior : IStreamPipelineBehavior<ShortCircuitStreamRequest, string>
    {
        private readonly List<string> _trace;

        public ShortCircuitStreamBehavior(List<string> trace)
        {
            _trace = trace;
        }

        public async IAsyncEnumerable<string> Handle(ShortCircuitStreamRequest request, StreamHandlerDelegate<string> next, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            _trace.Add("behavior");
            cancellationToken.ThrowIfCancellationRequested();
            yield return "short-circuited";
            await Task.Yield();
        }
    }

    private sealed record MultiShortCircuitStreamRequest : IStreamRequest<string>;

    private sealed class MultiShortCircuitStreamHandler : IStreamRequestHandler<MultiShortCircuitStreamRequest, string>
    {
        private readonly List<string> _trace;

        public MultiShortCircuitStreamHandler(List<string> trace)
        {
            _trace = trace;
        }

        public async IAsyncEnumerable<string> Handle(MultiShortCircuitStreamRequest request, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            _trace.Add("handler");
            cancellationToken.ThrowIfCancellationRequested();
            yield return "handler-value";
            await Task.Yield();
        }
    }

    private sealed class OuterMultiShortCircuitStreamBehavior : IStreamPipelineBehavior<MultiShortCircuitStreamRequest, string>
    {
        private readonly List<string> _trace;

        public OuterMultiShortCircuitStreamBehavior(List<string> trace)
        {
            _trace = trace;
        }

        public async IAsyncEnumerable<string> Handle(MultiShortCircuitStreamRequest request, StreamHandlerDelegate<string> next, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            _trace.Add("outer-before");

            await foreach (var item in next().WithCancellation(cancellationToken))
            {
                yield return item;
            }

            _trace.Add("outer-after");
        }
    }

    private sealed class InnerMultiShortCircuitStreamBehavior : IStreamPipelineBehavior<MultiShortCircuitStreamRequest, string>
    {
        private readonly List<string> _trace;

        public InnerMultiShortCircuitStreamBehavior(List<string> trace)
        {
            _trace = trace;
        }

        public async IAsyncEnumerable<string> Handle(MultiShortCircuitStreamRequest request, StreamHandlerDelegate<string> next, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            _trace.Add("inner-short");
            cancellationToken.ThrowIfCancellationRequested();
            yield return "inner-short-circuited";
            await Task.Yield();
        }
    }

    private sealed class StreamCallCounter
    {
        public int Count { get; set; }
    }

    private sealed record CancellableBehaviorStreamRequest : IStreamRequest<string>;

    private sealed class CancellableBehaviorStreamHandler : IStreamRequestHandler<CancellableBehaviorStreamRequest, string>
    {
        private readonly StreamCallCounter _counter;

        public CancellableBehaviorStreamHandler(StreamCallCounter counter)
        {
            _counter = counter;
        }

        public async IAsyncEnumerable<string> Handle(CancellableBehaviorStreamRequest request, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            _counter.Count++;
            cancellationToken.ThrowIfCancellationRequested();
            yield return "ok";
            await Task.Yield();
        }
    }

    private sealed class CancellationAwareStreamBehavior : IStreamPipelineBehavior<CancellableBehaviorStreamRequest, string>
    {
        public async IAsyncEnumerable<string> Handle(CancellableBehaviorStreamRequest request, StreamHandlerDelegate<string> next, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            await foreach (var item in next().WithCancellation(cancellationToken))
            {
                yield return item;
            }
        }
    }

    private sealed record ConcurrentBehaviorStreamRequest(int Value) : IStreamRequest<int>;

    private sealed class ConcurrentBehaviorStreamHandler : IStreamRequestHandler<ConcurrentBehaviorStreamRequest, int>
    {
        public async IAsyncEnumerable<int> Handle(ConcurrentBehaviorStreamRequest request, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return request.Value;
            await Task.Yield();
        }
    }

    private sealed class ConcurrentPassThroughStreamBehavior : IStreamPipelineBehavior<ConcurrentBehaviorStreamRequest, int>
    {
        public async IAsyncEnumerable<int> Handle(ConcurrentBehaviorStreamRequest request, StreamHandlerDelegate<int> next, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await foreach (int item in next().WithCancellation(cancellationToken))
            {
                yield return item;
            }
        }
    }

    private sealed record CancellableDuringEnumerationRequest : IStreamRequest<string>;

    private sealed class CancellableDuringEnumerationHandler : IStreamRequestHandler<CancellableDuringEnumerationRequest, string>
    {
        private readonly List<string> _trace;

        public CancellableDuringEnumerationHandler(List<string> trace)
        {
            _trace = trace;
        }

        public async IAsyncEnumerable<string> Handle(CancellableDuringEnumerationRequest request, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            _trace.Add("handler-yield-1");
            yield return "first";
            await Task.Yield();

            try
            {
                await Task.Delay(100, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                _trace.Add("handler-cancelled");
                throw;
            }

            _trace.Add("handler-yield-2");
            yield return "second";
        }
    }

    private sealed class CancellableDuringEnumerationBehavior : IStreamPipelineBehavior<CancellableDuringEnumerationRequest, string>
    {
        private readonly List<string> _trace;

        public CancellableDuringEnumerationBehavior(List<string> trace)
        {
            _trace = trace;
        }

        public async IAsyncEnumerable<string> Handle(CancellableDuringEnumerationRequest request, StreamHandlerDelegate<string> next, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            _trace.Add("behavior-before");

            await foreach (string item in next().WithCancellation(cancellationToken))
            {
                _trace.Add("behavior-yield");
                yield return item;
            }

            _trace.Add("behavior-after");
        }
    }

    private static class OpenGenericStreamRecorder
    {
        private static readonly List<string> Steps = [];

        public static void Clear()
        {
            lock (Steps)
            {
                Steps.Clear();
            }
        }

        public static void Add(string step)
        {
            lock (Steps)
            {
                Steps.Add(step);
            }
        }

        public static string[] GetSteps()
        {
            lock (Steps)
            {
                return Steps.ToArray();
            }
        }
    }

    private sealed record OpenGenericStreamRequest : IStreamRequest<string>;

    private sealed class OpenGenericStreamRequestHandler : IStreamRequestHandler<OpenGenericStreamRequest, string>
    {
        public async IAsyncEnumerable<string> Handle(OpenGenericStreamRequest request, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            OpenGenericStreamRecorder.Add("handler");
            cancellationToken.ThrowIfCancellationRequested();
            yield return "ok";
            await Task.Yield();
        }
    }

    private sealed class OpenGenericStreamBehavior<TRequest, TResponse> : IStreamPipelineBehavior<TRequest, TResponse>
        where TRequest : IStreamRequest<TResponse>
    {
        public async IAsyncEnumerable<TResponse> Handle(TRequest request, StreamHandlerDelegate<TResponse> next, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var shouldRecord = request is OpenGenericStreamRequest;

            if (shouldRecord)
            {
                OpenGenericStreamRecorder.Add("before");
            }

            await foreach (var item in next().WithCancellation(cancellationToken))
            {
                yield return item;
            }

            if (shouldRecord)
            {
                OpenGenericStreamRecorder.Add("after");
            }
        }
    }

    private static class ConfiguredStreamBehaviorRecorder
    {
        private static readonly List<string> Steps = [];

        public static void Clear()
        {
            lock (Steps)
            {
                Steps.Clear();
            }
        }

        public static void Add(string step)
        {
            lock (Steps)
            {
                Steps.Add(step);
            }
        }

        public static string[] GetSteps()
        {
            lock (Steps)
            {
                return Steps.ToArray();
            }
        }
    }

    private sealed record ConfiguredOrderStreamRequest : IStreamRequest<string>;

    private sealed class ConfiguredOrderStreamRequestHandler : IStreamRequestHandler<ConfiguredOrderStreamRequest, string>
    {
        public async IAsyncEnumerable<string> Handle(ConfiguredOrderStreamRequest request, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            ConfiguredStreamBehaviorRecorder.Add("handler");
            cancellationToken.ThrowIfCancellationRequested();
            yield return "ok";
            await Task.Yield();
        }
    }

    private sealed class ConfiguredFirstStreamBehavior<TRequest, TResponse> : IStreamPipelineBehavior<TRequest, TResponse>
        where TRequest : IStreamRequest<TResponse>
    {
        public async IAsyncEnumerable<TResponse> Handle(TRequest request, StreamHandlerDelegate<TResponse> next, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            bool shouldRecord = request is ConfiguredOrderStreamRequest;
            if (shouldRecord)
            {
                ConfiguredStreamBehaviorRecorder.Add("first-before");
            }

            await foreach (TResponse item in next().WithCancellation(cancellationToken))
            {
                yield return item;
            }

            if (shouldRecord)
            {
                ConfiguredStreamBehaviorRecorder.Add("first-after");
            }
        }
    }

    private sealed class ConfiguredSecondStreamBehavior<TRequest, TResponse> : IStreamPipelineBehavior<TRequest, TResponse>
        where TRequest : IStreamRequest<TResponse>
    {
        public async IAsyncEnumerable<TResponse> Handle(TRequest request, StreamHandlerDelegate<TResponse> next, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            bool shouldRecord = request is ConfiguredOrderStreamRequest;
            if (shouldRecord)
            {
                ConfiguredStreamBehaviorRecorder.Add("second-before");
            }

            await foreach (TResponse item in next().WithCancellation(cancellationToken))
            {
                yield return item;
            }

            if (shouldRecord)
            {
                ConfiguredStreamBehaviorRecorder.Add("second-after");
            }
        }
    }

    private sealed class ConfiguredClosedStreamBehavior : IStreamPipelineBehavior<ConfiguredOrderStreamRequest, string>
    {
        public async IAsyncEnumerable<string> Handle(ConfiguredOrderStreamRequest request, StreamHandlerDelegate<string> next, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            ConfiguredStreamBehaviorRecorder.Add("closed-before");

            await foreach (string item in next().WithCancellation(cancellationToken))
            {
                yield return item;
            }

            ConfiguredStreamBehaviorRecorder.Add("closed-after");
        }
    }
}
