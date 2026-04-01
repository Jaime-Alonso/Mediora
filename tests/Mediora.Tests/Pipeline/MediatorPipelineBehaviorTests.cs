using Microsoft.Extensions.DependencyInjection;

namespace Mediora.Tests;

public sealed class MediatorPipelineBehaviorTests
{
    [Fact]
    public async Task Send_SingleBehaviorWrapsHandlerExecution()
    {
        var trace = new List<string>();
        var services = new ServiceCollection();
        services.AddSingleton(trace);
        services.AddSingleton<IRequestHandler<BehaviorRequest, string>, BehaviorHandler>();
        services.AddSingleton<IPipelineBehavior<BehaviorRequest, string>, SingleTraceBehavior>();
        services.AddSingleton<IMediator, Mediator>();

        await using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        _ = await mediator.Send(new BehaviorRequest());

        Assert.Equal(new[] { "before", "handler", "after" }, trace);
    }

    [Fact]
    public async Task Send_MultipleBehaviorsExecuteInRegistrationOrder()
    {
        var trace = new List<string>();
        var services = new ServiceCollection();
        services.AddSingleton(trace);
        services.AddSingleton<IRequestHandler<OrderRequest, string>, OrderHandler>();
        services.AddSingleton<IPipelineBehavior<OrderRequest, string>, OuterBehavior>();
        services.AddSingleton<IPipelineBehavior<OrderRequest, string>, InnerBehavior>();
        services.AddSingleton<IMediator, Mediator>();

        await using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        _ = await mediator.Send(new OrderRequest());

        Assert.Equal(new[] { "outer-before", "inner-before", "handler", "inner-after", "outer-after" }, trace);
    }

    [Fact]
    public async Task Send_BehaviorCanShortCircuitPipeline()
    {
        var trace = new List<string>();
        var services = new ServiceCollection();
        services.AddSingleton(trace);
        services.AddSingleton<IRequestHandler<ShortCircuitRequest, string>, ShortCircuitHandler>();
        services.AddSingleton<IPipelineBehavior<ShortCircuitRequest, string>, ShortCircuitBehavior>();
        services.AddSingleton<IMediator, Mediator>();

        await using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        var response = await mediator.Send(new ShortCircuitRequest());

        Assert.Equal("short-circuited", response);
        Assert.Equal(new[] { "behavior" }, trace);
    }

    [Fact]
    public async Task Send_MultipleBehaviors_WithShortCircuit_SkipsInnerAndHandler()
    {
        var trace = new List<string>();
        var services = new ServiceCollection();
        services.AddSingleton(trace);
        services.AddSingleton<IRequestHandler<MultiShortCircuitRequest, string>, MultiShortCircuitHandler>();
        services.AddSingleton<IPipelineBehavior<MultiShortCircuitRequest, string>, OuterShortCircuitBehavior>();
        services.AddSingleton<IPipelineBehavior<MultiShortCircuitRequest, string>, InnerShortCircuitBehavior>();
        services.AddSingleton<IMediator, Mediator>();

        await using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        var response = await mediator.Send(new MultiShortCircuitRequest());

        Assert.Equal("inner-short-circuit", response);
        Assert.Equal(new[] { "outer-before", "inner-short", "outer-after" }, trace);
    }

    [Fact]
    public async Task Send_BehaviorReceivesCancellation_AndStopsBeforeNext()
    {
        var counter = new CallCounter();
        var services = new ServiceCollection();
        services.AddSingleton(counter);
        services.AddSingleton<IRequestHandler<CancellableBehaviorRequest, string>, CancellableBehaviorHandler>();
        services.AddSingleton<IPipelineBehavior<CancellableBehaviorRequest, string>, CancellationAwareBehavior>();
        services.AddSingleton<IMediator, Mediator>();

        await using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => mediator.Send(new CancellableBehaviorRequest(), cancellation.Token));
        Assert.Equal(0, counter.Count);
    }

    [Fact]
    public async Task Send_BehaviorCanModifyResponse()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IRequestHandler<ModifyRequest, string>, ModifyHandler>();
        services.AddSingleton<IPipelineBehavior<ModifyRequest, string>, SuffixBehavior>();
        services.AddSingleton<IMediator, Mediator>();

        await using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        var response = await mediator.Send(new ModifyRequest());

        Assert.Equal("handler-response-behavior", response);
    }

    [Fact]
    public async Task Send_WithoutBehaviorsInvokesHandlerDirectly()
    {
        var counter = new CallCounter();
        var services = new ServiceCollection();
        services.AddSingleton(counter);
        services.AddSingleton<IRequestHandler<DirectRequest, string>, DirectHandler>();
        services.AddSingleton<IMediator, Mediator>();

        await using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        var response = await mediator.Send(new DirectRequest());

        Assert.Equal("direct", response);
        Assert.Equal(1, counter.Count);
    }

    [Fact]
    public async Task Send_Throws_WhenBehaviorFailsBeforeCallingNext()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IRequestHandler<FailBeforeRequest, string>, FailBeforeHandler>();
        services.AddSingleton<IPipelineBehavior<FailBeforeRequest, string>, FailingBeforeBehavior>();
        services.AddSingleton<IMediator, Mediator>();

        await using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => mediator.Send(new FailBeforeRequest()));

        Assert.Equal("before-failure", exception.Message);
    }

    [Fact]
    public async Task Send_Throws_WhenBehaviorFailsAfterCallingNext()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IRequestHandler<FailAfterRequest, string>, FailAfterHandler>();
        services.AddSingleton<IPipelineBehavior<FailAfterRequest, string>, FailingAfterBehavior>();
        services.AddSingleton<IMediator, Mediator>();

        await using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => mediator.Send(new FailAfterRequest()));

        Assert.Equal("after-failure", exception.Message);
    }

    [Fact]
    public async Task AddMediora_ResolvesAndExecutesOpenGenericPipelineBehavior()
    {
        var services = new ServiceCollection();
        services.AddMediora(options =>
        {
            options.RegisterServicesFromAssembly(typeof(OpenGenericRequestHandler).Assembly);
            options.AddOpenBehavior(typeof(OpenGenericBehavior<,>));
        });

        await using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();
        OpenGenericRecorder.Clear();

        _ = await mediator.Send(new OpenGenericRequest());

        Assert.Equal(new[] { "before", "handler", "after" }, OpenGenericRecorder.GetSteps());
    }

    [Fact]
    public async Task AddMediora_MultipleConfiguredBehaviors_ExecuteInConfigurationOrder()
    {
        var services = new ServiceCollection();
        services.AddMediora(options =>
        {
            options.RegisterServicesFromAssembly(typeof(OpenGenericRequestHandler).Assembly);
            options.AddOpenBehavior(typeof(ConfiguredFirstBehavior<,>));
            options.AddOpenBehavior(typeof(ConfiguredSecondBehavior<,>));
        });

        await using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();
        ConfiguredBehaviorRecorder.Clear();

        _ = await mediator.Send(new ConfiguredOrderRequest());

        Assert.Equal(
            new[] { "first-before", "second-before", "handler", "second-after", "first-after" },
            ConfiguredBehaviorRecorder.GetSteps());
    }

    [Fact]
    public async Task AddMediora_ConfiguredClosedAndOpenBehaviors_ExecuteInConfigurationOrder()
    {
        var services = new ServiceCollection();
        services.AddMediora(options =>
        {
            options.RegisterServicesFromAssembly(typeof(OpenGenericRequestHandler).Assembly);
            options.AddBehavior<ConfiguredClosedBehavior>();
            options.AddOpenBehavior(typeof(ConfiguredSecondBehavior<,>));
        });

        await using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();
        ConfiguredBehaviorRecorder.Clear();

        _ = await mediator.Send(new ConfiguredOrderRequest());

        Assert.Equal(
            new[] { "closed-before", "second-before", "handler", "second-after", "closed-after" },
            ConfiguredBehaviorRecorder.GetSteps());
    }

    [Fact]
    public async Task AddMediora_ConfiguredOpenThenClosedBehaviors_ExecuteInConfigurationOrder()
    {
        var services = new ServiceCollection();
        services.AddMediora(options =>
        {
            options.RegisterServicesFromAssembly(typeof(OpenGenericRequestHandler).Assembly);
            options.AddOpenBehavior(typeof(ConfiguredFirstBehavior<,>));
            options.AddBehavior<ConfiguredClosedBehavior>();
        });

        await using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();
        ConfiguredBehaviorRecorder.Clear();

        _ = await mediator.Send(new ConfiguredOrderRequest());

        Assert.Equal(
            new[] { "first-before", "closed-before", "handler", "closed-after", "first-after" },
            ConfiguredBehaviorRecorder.GetSteps());
    }

    [Fact]
    public async Task Send_OpenGenericAndClosedBehavior_ExecuteInRegistrationOrder()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IRequestHandler<CombinedBehaviorRequest, string>, CombinedBehaviorHandler>();
        services.AddSingleton(typeof(IPipelineBehavior<,>), typeof(CombinedOpenGenericBehavior<,>));
        services.AddSingleton<IPipelineBehavior<CombinedBehaviorRequest, string>, CombinedClosedBehavior>();
        services.AddSingleton<IMediator, Mediator>();

        await using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();
        CombinedBehaviorRecorder.Clear();

        _ = await mediator.Send(new CombinedBehaviorRequest());

        Assert.Equal(
            new[] { "open-before", "closed-before", "handler", "closed-after", "open-after" },
            CombinedBehaviorRecorder.GetSteps());
    }

    [Fact]
    public async Task Send_MultipleOpenGenericBehaviors_ExecuteInRegistrationOrder()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IRequestHandler<OrderedOpenGenericRequest, string>, OrderedOpenGenericHandler>();
        services.AddSingleton(typeof(IPipelineBehavior<,>), typeof(FirstOrderedOpenGenericBehavior<,>));
        services.AddSingleton(typeof(IPipelineBehavior<,>), typeof(SecondOrderedOpenGenericBehavior<,>));
        services.AddSingleton<IMediator, Mediator>();

        await using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();
        OrderedOpenGenericRecorder.Clear();

        _ = await mediator.Send(new OrderedOpenGenericRequest());

        Assert.Equal(
            new[] { "first-before", "second-before", "handler", "second-after", "first-after" },
            OrderedOpenGenericRecorder.GetSteps());
    }

    [Fact]
    public async Task Send_BehaviorThrowsAfterNext_PropagatesAndSkipsOuterAfter()
    {
        var trace = new List<string>();
        var services = new ServiceCollection();
        services.AddSingleton(trace);
        services.AddSingleton<IRequestHandler<ThrowAfterNextRequest, string>, ThrowAfterNextHandler>();
        services.AddSingleton<IPipelineBehavior<ThrowAfterNextRequest, string>, OuterAfterSkipBehavior>();
        services.AddSingleton<IPipelineBehavior<ThrowAfterNextRequest, string>, ThrowAfterNextBehavior>();
        services.AddSingleton<IMediator, Mediator>();

        await using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(() => mediator.Send(new ThrowAfterNextRequest()));

        Assert.Equal("throw-after-next", exception.Message);
        Assert.Equal(new[] { "outer-before", "inner-before", "handler", "inner-after" }, trace);
    }

    private sealed record BehaviorRequest : IRequest<string>;

    private sealed class BehaviorHandler : IRequestHandler<BehaviorRequest, string>
    {
        private readonly List<string> _trace;

        public BehaviorHandler(List<string> trace)
        {
            _trace = trace;
        }

        public Task<string> Handle(BehaviorRequest request, CancellationToken cancellationToken)
        {
            _trace.Add("handler");
            return Task.FromResult("ok");
        }
    }

    private sealed class SingleTraceBehavior : IPipelineBehavior<BehaviorRequest, string>
    {
        private readonly List<string> _trace;

        public SingleTraceBehavior(List<string> trace)
        {
            _trace = trace;
        }

        public async Task<string> Handle(BehaviorRequest request, RequestHandlerDelegate<string> next, CancellationToken cancellationToken)
        {
            _trace.Add("before");
            var result = await next();
            _trace.Add("after");
            return result;
        }
    }

    private sealed record OrderRequest : IRequest<string>;

    private sealed class OrderHandler : IRequestHandler<OrderRequest, string>
    {
        private readonly List<string> _trace;

        public OrderHandler(List<string> trace)
        {
            _trace = trace;
        }

        public Task<string> Handle(OrderRequest request, CancellationToken cancellationToken)
        {
            _trace.Add("handler");
            return Task.FromResult("done");
        }
    }

    private sealed class OuterBehavior : IPipelineBehavior<OrderRequest, string>
    {
        private readonly List<string> _trace;

        public OuterBehavior(List<string> trace)
        {
            _trace = trace;
        }

        public async Task<string> Handle(OrderRequest request, RequestHandlerDelegate<string> next, CancellationToken cancellationToken)
        {
            _trace.Add("outer-before");
            var result = await next();
            _trace.Add("outer-after");
            return result;
        }
    }

    private sealed class InnerBehavior : IPipelineBehavior<OrderRequest, string>
    {
        private readonly List<string> _trace;

        public InnerBehavior(List<string> trace)
        {
            _trace = trace;
        }

        public async Task<string> Handle(OrderRequest request, RequestHandlerDelegate<string> next, CancellationToken cancellationToken)
        {
            _trace.Add("inner-before");
            var result = await next();
            _trace.Add("inner-after");
            return result;
        }
    }

    private sealed record ShortCircuitRequest : IRequest<string>;

    private sealed class ShortCircuitHandler : IRequestHandler<ShortCircuitRequest, string>
    {
        private readonly List<string> _trace;

        public ShortCircuitHandler(List<string> trace)
        {
            _trace = trace;
        }

        public Task<string> Handle(ShortCircuitRequest request, CancellationToken cancellationToken)
        {
            _trace.Add("handler");
            return Task.FromResult("handler");
        }
    }

    private sealed class ShortCircuitBehavior : IPipelineBehavior<ShortCircuitRequest, string>
    {
        private readonly List<string> _trace;

        public ShortCircuitBehavior(List<string> trace)
        {
            _trace = trace;
        }

        public Task<string> Handle(ShortCircuitRequest request, RequestHandlerDelegate<string> next, CancellationToken cancellationToken)
        {
            _trace.Add("behavior");
            return Task.FromResult("short-circuited");
        }
    }

    private sealed record MultiShortCircuitRequest : IRequest<string>;

    private sealed class MultiShortCircuitHandler : IRequestHandler<MultiShortCircuitRequest, string>
    {
        private readonly List<string> _trace;

        public MultiShortCircuitHandler(List<string> trace)
        {
            _trace = trace;
        }

        public Task<string> Handle(MultiShortCircuitRequest request, CancellationToken cancellationToken)
        {
            _trace.Add("handler");
            return Task.FromResult("handler");
        }
    }

    private sealed class OuterShortCircuitBehavior : IPipelineBehavior<MultiShortCircuitRequest, string>
    {
        private readonly List<string> _trace;

        public OuterShortCircuitBehavior(List<string> trace)
        {
            _trace = trace;
        }

        public async Task<string> Handle(MultiShortCircuitRequest request, RequestHandlerDelegate<string> next, CancellationToken cancellationToken)
        {
            _trace.Add("outer-before");
            string response = await next();
            _trace.Add("outer-after");
            return response;
        }
    }

    private sealed class InnerShortCircuitBehavior : IPipelineBehavior<MultiShortCircuitRequest, string>
    {
        private readonly List<string> _trace;

        public InnerShortCircuitBehavior(List<string> trace)
        {
            _trace = trace;
        }

        public Task<string> Handle(MultiShortCircuitRequest request, RequestHandlerDelegate<string> next, CancellationToken cancellationToken)
        {
            _trace.Add("inner-short");
            return Task.FromResult("inner-short-circuit");
        }
    }

    private sealed record CancellableBehaviorRequest : IRequest<string>;

    private sealed class CancellableBehaviorHandler : IRequestHandler<CancellableBehaviorRequest, string>
    {
        private readonly CallCounter _counter;

        public CancellableBehaviorHandler(CallCounter counter)
        {
            _counter = counter;
        }

        public Task<string> Handle(CancellableBehaviorRequest request, CancellationToken cancellationToken)
        {
            _counter.Count++;
            return Task.FromResult("ok");
        }
    }

    private sealed class CancellationAwareBehavior : IPipelineBehavior<CancellableBehaviorRequest, string>
    {
        public Task<string> Handle(CancellableBehaviorRequest request, RequestHandlerDelegate<string> next, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return next();
        }
    }

    private sealed record ModifyRequest : IRequest<string>;

    private sealed class ModifyHandler : IRequestHandler<ModifyRequest, string>
    {
        public Task<string> Handle(ModifyRequest request, CancellationToken cancellationToken)
        {
            return Task.FromResult("handler-response");
        }
    }

    private sealed class SuffixBehavior : IPipelineBehavior<ModifyRequest, string>
    {
        public async Task<string> Handle(ModifyRequest request, RequestHandlerDelegate<string> next, CancellationToken cancellationToken)
        {
            var result = await next();
            return result + "-behavior";
        }
    }

    private sealed record DirectRequest : IRequest<string>;

    private sealed class CallCounter
    {
        public int Count { get; set; }
    }

    private sealed class DirectHandler : IRequestHandler<DirectRequest, string>
    {
        private readonly CallCounter _counter;

        public DirectHandler(CallCounter counter)
        {
            _counter = counter;
        }

        public Task<string> Handle(DirectRequest request, CancellationToken cancellationToken)
        {
            _counter.Count++;
            return Task.FromResult("direct");
        }
    }

    private sealed record FailBeforeRequest : IRequest<string>;

    private sealed class FailBeforeHandler : IRequestHandler<FailBeforeRequest, string>
    {
        public Task<string> Handle(FailBeforeRequest request, CancellationToken cancellationToken)
        {
            return Task.FromResult("ok");
        }
    }

    private sealed class FailingBeforeBehavior : IPipelineBehavior<FailBeforeRequest, string>
    {
        public Task<string> Handle(FailBeforeRequest request, RequestHandlerDelegate<string> next, CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("before-failure");
        }
    }

    private sealed record FailAfterRequest : IRequest<string>;

    private sealed class FailAfterHandler : IRequestHandler<FailAfterRequest, string>
    {
        public Task<string> Handle(FailAfterRequest request, CancellationToken cancellationToken)
        {
            return Task.FromResult("ok");
        }
    }

    private sealed class FailingAfterBehavior : IPipelineBehavior<FailAfterRequest, string>
    {
        public async Task<string> Handle(FailAfterRequest request, RequestHandlerDelegate<string> next, CancellationToken cancellationToken)
        {
            _ = await next();
            throw new InvalidOperationException("after-failure");
        }
    }

    private static class OpenGenericRecorder
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

    private static class CombinedBehaviorRecorder
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

    private sealed record CombinedBehaviorRequest : IRequest<string>;

    private sealed class CombinedBehaviorHandler : IRequestHandler<CombinedBehaviorRequest, string>
    {
        public Task<string> Handle(CombinedBehaviorRequest request, CancellationToken cancellationToken)
        {
            CombinedBehaviorRecorder.Add("handler");
            return Task.FromResult("ok");
        }
    }

    private sealed class CombinedOpenGenericBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
        where TRequest : IRequest<TResponse>
    {
        public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
        {
            bool shouldRecord = request is CombinedBehaviorRequest;
            if (shouldRecord)
            {
                CombinedBehaviorRecorder.Add("open-before");
            }

            TResponse response = await next();

            if (shouldRecord)
            {
                CombinedBehaviorRecorder.Add("open-after");
            }

            return response;
        }
    }

    private sealed class CombinedClosedBehavior : IPipelineBehavior<CombinedBehaviorRequest, string>
    {
        public async Task<string> Handle(CombinedBehaviorRequest request, RequestHandlerDelegate<string> next, CancellationToken cancellationToken)
        {
            CombinedBehaviorRecorder.Add("closed-before");
            string response = await next();
            CombinedBehaviorRecorder.Add("closed-after");
            return response;
        }
    }

    private static class OrderedOpenGenericRecorder
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

    private sealed record OrderedOpenGenericRequest : IRequest<string>;

    private sealed class OrderedOpenGenericHandler : IRequestHandler<OrderedOpenGenericRequest, string>
    {
        public Task<string> Handle(OrderedOpenGenericRequest request, CancellationToken cancellationToken)
        {
            OrderedOpenGenericRecorder.Add("handler");
            return Task.FromResult("ok");
        }
    }

    private sealed class FirstOrderedOpenGenericBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
        where TRequest : IRequest<TResponse>
    {
        public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
        {
            bool shouldRecord = request is OrderedOpenGenericRequest;
            if (shouldRecord)
            {
                OrderedOpenGenericRecorder.Add("first-before");
            }

            TResponse response = await next();

            if (shouldRecord)
            {
                OrderedOpenGenericRecorder.Add("first-after");
            }

            return response;
        }
    }

    private sealed class SecondOrderedOpenGenericBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
        where TRequest : IRequest<TResponse>
    {
        public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
        {
            bool shouldRecord = request is OrderedOpenGenericRequest;
            if (shouldRecord)
            {
                OrderedOpenGenericRecorder.Add("second-before");
            }

            TResponse response = await next();

            if (shouldRecord)
            {
                OrderedOpenGenericRecorder.Add("second-after");
            }

            return response;
        }
    }

    private sealed record ThrowAfterNextRequest : IRequest<string>;

    private sealed class ThrowAfterNextHandler : IRequestHandler<ThrowAfterNextRequest, string>
    {
        private readonly List<string> _trace;

        public ThrowAfterNextHandler(List<string> trace)
        {
            _trace = trace;
        }

        public Task<string> Handle(ThrowAfterNextRequest request, CancellationToken cancellationToken)
        {
            _trace.Add("handler");
            return Task.FromResult("ok");
        }
    }

    private sealed class OuterAfterSkipBehavior : IPipelineBehavior<ThrowAfterNextRequest, string>
    {
        private readonly List<string> _trace;

        public OuterAfterSkipBehavior(List<string> trace)
        {
            _trace = trace;
        }

        public async Task<string> Handle(ThrowAfterNextRequest request, RequestHandlerDelegate<string> next, CancellationToken cancellationToken)
        {
            _trace.Add("outer-before");
            string response = await next();
            _trace.Add("outer-after");
            return response;
        }
    }

    private sealed class ThrowAfterNextBehavior : IPipelineBehavior<ThrowAfterNextRequest, string>
    {
        private readonly List<string> _trace;

        public ThrowAfterNextBehavior(List<string> trace)
        {
            _trace = trace;
        }

        public async Task<string> Handle(ThrowAfterNextRequest request, RequestHandlerDelegate<string> next, CancellationToken cancellationToken)
        {
            _trace.Add("inner-before");
            _ = await next();
            _trace.Add("inner-after");
            throw new InvalidOperationException("throw-after-next");
        }
    }

    private static class ConfiguredBehaviorRecorder
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

    private sealed record ConfiguredOrderRequest : IRequest<string>;

    private sealed class ConfiguredOrderRequestHandler : IRequestHandler<ConfiguredOrderRequest, string>
    {
        public Task<string> Handle(ConfiguredOrderRequest request, CancellationToken cancellationToken)
        {
            ConfiguredBehaviorRecorder.Add("handler");
            return Task.FromResult("ok");
        }
    }

    private sealed class ConfiguredFirstBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
        where TRequest : IRequest<TResponse>
    {
        public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
        {
            bool shouldRecord = request is ConfiguredOrderRequest;
            if (shouldRecord)
            {
                ConfiguredBehaviorRecorder.Add("first-before");
            }

            TResponse response = await next();

            if (shouldRecord)
            {
                ConfiguredBehaviorRecorder.Add("first-after");
            }

            return response;
        }
    }

    private sealed class ConfiguredSecondBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
        where TRequest : IRequest<TResponse>
    {
        public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
        {
            bool shouldRecord = request is ConfiguredOrderRequest;
            if (shouldRecord)
            {
                ConfiguredBehaviorRecorder.Add("second-before");
            }

            TResponse response = await next();

            if (shouldRecord)
            {
                ConfiguredBehaviorRecorder.Add("second-after");
            }

            return response;
        }
    }

    private sealed class ConfiguredClosedBehavior : IPipelineBehavior<ConfiguredOrderRequest, string>
    {
        public async Task<string> Handle(ConfiguredOrderRequest request, RequestHandlerDelegate<string> next, CancellationToken cancellationToken)
        {
            ConfiguredBehaviorRecorder.Add("closed-before");
            string response = await next();
            ConfiguredBehaviorRecorder.Add("closed-after");
            return response;
        }
    }

    private sealed record OpenGenericRequest : IRequest<string>;

    private sealed class OpenGenericRequestHandler : IRequestHandler<OpenGenericRequest, string>
    {
        public Task<string> Handle(OpenGenericRequest request, CancellationToken cancellationToken)
        {
            OpenGenericRecorder.Add("handler");
            return Task.FromResult("ok");
        }
    }

    private sealed class OpenGenericBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
        where TRequest : IRequest<TResponse>
    {
        public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
        {
            var shouldRecord = request is OpenGenericRequest;

            if (shouldRecord)
            {
                OpenGenericRecorder.Add("before");
            }

            var response = await next();

            if (shouldRecord)
            {
                OpenGenericRecorder.Add("after");
            }

            return response;
        }
    }
}
