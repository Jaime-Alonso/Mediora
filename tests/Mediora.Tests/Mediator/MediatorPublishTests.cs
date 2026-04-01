using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;

namespace Mediora.Tests;

public sealed class MediatorPublishTests
{
    [Fact]
    public async Task Publish_WithMultipleHandlers_InvokesAllRegisteredHandlers()
    {
        var calls = new List<string>();
        var services = new ServiceCollection();
        services.AddSingleton(calls);
        services.AddSingleton<INotificationHandler<OrderPlacedNotification>, FirstNotificationHandler>();
        services.AddSingleton<INotificationHandler<OrderPlacedNotification>, SecondNotificationHandler>();
        services.AddSingleton<IMediator, Mediator>();

        await using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        await mediator.Publish(new OrderPlacedNotification());

        Assert.Equal(2, calls.Count);
        Assert.Contains("first", calls);
        Assert.Contains("second", calls);
    }

    [Fact]
    public async Task Publish_WithoutHandlers_CompletesSuccessfully()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IMediator, Mediator>();

        await using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        await mediator.Publish(new NoHandlerNotification());
    }

    [Fact]
    public async Task Publish_ThrowsArgumentNullException_WhenNotificationIsNull()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IMediator, Mediator>();

        await using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        var exception = await Assert.ThrowsAsync<ArgumentNullException>(() => mediator.Publish(null!));

        Assert.Equal("notification", exception.ParamName);
    }

    [Fact]
    public async Task Publish_SequentialFailFast_InvokesHandlersInRegistrationOrder()
    {
        var calls = new List<string>();
        var services = new ServiceCollection();
        services.AddSingleton(calls);
        services.AddSingleton<INotificationHandler<OrderedNotification>, OrderedFirstHandler>();
        services.AddSingleton<INotificationHandler<OrderedNotification>, OrderedSecondHandler>();
        services.AddSingleton<IMediator, Mediator>();

        await using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        await mediator.Publish(new OrderedNotification());

        Assert.Equal(new[] { "1", "2" }, calls);
    }

    [Fact]
    public async Task Publish_GenericOverload_InvokesHandlers()
    {
        var calls = new List<string>();
        var services = new ServiceCollection();
        services.AddSingleton(calls);
        services.AddSingleton<INotificationHandler<TypedNotification>, TypedNotificationHandler>();
        services.AddSingleton<IPublisher, Mediator>();

        await using var provider = services.BuildServiceProvider();
        var publisher = provider.GetRequiredService<IPublisher>();

        await publisher.Publish(new TypedNotification());

        Assert.Equal(new[] { "typed" }, calls);
    }

    [Fact]
    public async Task Publish_NonGenericOverload_UsesRuntimeNotificationTypeHandlers()
    {
        var calls = new List<string>();
        var services = new ServiceCollection();
        services.AddSingleton(calls);
        services.AddSingleton<INotificationHandler<RuntimeTypedNotification>, RuntimeTypedNotificationHandler>();
        services.AddSingleton<IMediator, Mediator>();

        await using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        INotification notification = new RuntimeTypedNotification();
        await mediator.Publish(notification);

        Assert.Equal(["runtime-typed"], calls);
    }

    [Fact]
    public async Task Publish_DefaultSequentialFailFast_StopsOnFirstFailure()
    {
        var calls = new List<string>();
        var services = new ServiceCollection();
        services.AddSingleton(calls);
        services.AddSingleton<INotificationHandler<FaultyNotification>, FirstFailingHandler>();
        services.AddSingleton<INotificationHandler<FaultyNotification>, SecondFailingHandler>();
        services.AddSingleton<IMediator, Mediator>();

        await using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => mediator.Publish(new FaultyNotification()));

        Assert.Equal(["first"], calls);
        Assert.Equal("first", exception.Message);
    }

    [Fact]
    public async Task Publish_SequentialFailFast_StopsOnFirstCancellation()
    {
        var calls = new List<string>();
        var services = new ServiceCollection();
        services.AddSingleton(calls);
        services.AddSingleton<INotificationHandler<CancellableSequentialNotification>, CancellableSequentialFirstHandler>();
        services.AddSingleton<INotificationHandler<CancellableSequentialNotification>, CancellableSequentialSecondHandler>();
        services.AddSingleton(new NotificationPublishOptions(
            NotificationPublishMode.SequentialFailFast,
            null,
            []));
        services.AddSingleton<IMediator, Mediator>();

        await using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => mediator.Publish(new CancellableSequentialNotification(), cancellation.Token));

        Assert.Equal(["seq-first"], calls);
    }

    [Fact]
    public async Task Publish_ParallelAggregateAll_RunsHandlersConcurrently()
    {
        var probe = new ParallelProbe();
        var services = new ServiceCollection();
        services.AddSingleton(probe);
        services.AddSingleton<INotificationHandler<ParallelNotification>, ParallelFirstHandler>();
        services.AddSingleton<INotificationHandler<ParallelNotification>, ParallelSecondHandler>();
        services.AddSingleton(new NotificationPublishOptions(
            NotificationPublishMode.SequentialFailFast,
            null,
            new Dictionary<Type, NotificationPublishMode>
            {
                [typeof(ParallelNotification)] = NotificationPublishMode.ParallelAggregateAll
            }));
        services.AddSingleton<IMediator, Mediator>();

        await using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        Task publishTask = mediator.Publish(new ParallelNotification());

        try
        {
            await probe.BothStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
        }
        finally
        {
            probe.Release.TrySetResult();
        }

        await publishTask;
        Assert.True(probe.MaxConcurrentHandlers >= 2);
    }

    [Fact]
    public async Task Publish_WithPerTypeModeOverrides_AppliesConfiguredModes()
    {
        var sequentialCalls = new List<string>();
        var parallelProbe = new ParallelProbe();
        var services = new ServiceCollection();
        services.AddSingleton(sequentialCalls);
        services.AddSingleton(parallelProbe);
        services.AddSingleton<INotificationHandler<MixedSequentialNotification>, MixedSequentialFirstHandler>();
        services.AddSingleton<INotificationHandler<MixedSequentialNotification>, MixedSequentialSecondHandler>();
        services.AddSingleton<INotificationHandler<MixedParallelNotification>, ParallelFirstHandler>();
        services.AddSingleton<INotificationHandler<MixedParallelNotification>, ParallelSecondHandler>();
        services.AddSingleton(new NotificationPublishOptions(
            NotificationPublishMode.SequentialFailFast,
            null,
            new Dictionary<Type, NotificationPublishMode>
            {
                [typeof(MixedParallelNotification)] = NotificationPublishMode.ParallelAggregateAll
            }));
        services.AddSingleton<IMediator, Mediator>();

        await using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        await mediator.Publish(new MixedSequentialNotification());

        Task parallelPublishTask = mediator.Publish(new MixedParallelNotification());

        try
        {
            await parallelProbe.BothStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
        }
        finally
        {
            parallelProbe.Release.TrySetResult();
        }

        await parallelPublishTask;

        Assert.Equal(["seq-1", "seq-2"], sequentialCalls);
        Assert.True(parallelProbe.MaxConcurrentHandlers >= 2);
    }

    [Fact]
    public async Task Publish_PerTypeOverride_IsExactByNotificationType_NotByBaseType()
    {
        var probe = new SingleConcurrencyProbe();
        var services = new ServiceCollection();
        services.AddSingleton(probe);
        services.AddSingleton<INotificationHandler<DerivedExactNotification>, DerivedExactFirstHandler>();
        services.AddSingleton<INotificationHandler<DerivedExactNotification>, DerivedExactSecondHandler>();
        services.AddSingleton(new NotificationPublishOptions(
            NotificationPublishMode.SequentialFailFast,
            null,
            new Dictionary<Type, NotificationPublishMode>
            {
                [typeof(BaseExactNotification)] = NotificationPublishMode.ParallelAggregateAll
            }));
        services.AddSingleton<IMediator, Mediator>();

        await using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        Task publishTask = mediator.Publish(new DerivedExactNotification());

        await probe.FirstStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
        Task finished = await Task.WhenAny(probe.SecondStarted.Task, Task.Delay(150));
        Assert.NotSame(probe.SecondStarted.Task, finished);

        probe.Release.TrySetResult();
        await publishTask;

        Assert.Equal(1, probe.MaxConcurrentHandlers);
    }

    [Fact]
    public async Task Publish_PerTypeOverrideToSequentialFailFast_WinsOverParallelDefault()
    {
        var calls = new List<string>();
        var services = new ServiceCollection();
        services.AddSingleton(calls);
        services.AddSingleton<INotificationHandler<FaultyNotification>, FirstFailingHandler>();
        services.AddSingleton<INotificationHandler<FaultyNotification>, SecondFailingHandler>();
        services.AddSingleton(new NotificationPublishOptions(
            NotificationPublishMode.ParallelAggregateAll,
            null,
            new Dictionary<Type, NotificationPublishMode>
            {
                [typeof(FaultyNotification)] = NotificationPublishMode.SequentialFailFast
            }));
        services.AddSingleton<IMediator, Mediator>();

        await using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => mediator.Publish(new FaultyNotification()));

        Assert.Equal("first", exception.Message);
        Assert.Equal(["first"], calls);
    }

    [Fact]
    public async Task Publish_NonGenericOverload_UsesRuntimeTypeOverrideMode()
    {
        var calls = new List<string>();
        var services = new ServiceCollection();
        services.AddSingleton(calls);
        services.AddSingleton<INotificationHandler<RuntimeOverrideNotification>, RuntimeOverrideFirstHandler>();
        services.AddSingleton<INotificationHandler<RuntimeOverrideNotification>, RuntimeOverrideSecondHandler>();
        services.AddSingleton(new NotificationPublishOptions(
            NotificationPublishMode.ParallelAggregateAll,
            null,
            new Dictionary<Type, NotificationPublishMode>
            {
                [typeof(RuntimeOverrideNotification)] = NotificationPublishMode.SequentialFailFast
            }));
        services.AddSingleton<IMediator, Mediator>();

        await using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        INotification notification = new RuntimeOverrideNotification();
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => mediator.Publish(notification));

        Assert.Equal("runtime-first-fail", exception.Message);
        Assert.Equal(["runtime-first"], calls);
    }

    [Fact]
    public async Task Publish_ParallelAggregateAll_UsesDefaultModeWithoutOverride()
    {
        var probe = new ParallelProbe();
        var services = new ServiceCollection();
        services.AddSingleton(probe);
        services.AddSingleton<INotificationHandler<DefaultParallelNotification>, DefaultParallelFirstHandler>();
        services.AddSingleton<INotificationHandler<DefaultParallelNotification>, DefaultParallelSecondHandler>();
        services.AddSingleton(new NotificationPublishOptions(
            NotificationPublishMode.ParallelAggregateAll,
            null,
            []));
        services.AddSingleton<IMediator, Mediator>();

        await using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        Task publishTask = mediator.Publish(new DefaultParallelNotification());

        try
        {
            await probe.BothStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
        }
        finally
        {
            probe.Release.TrySetResult();
        }

        await publishTask;
        Assert.True(probe.MaxConcurrentHandlers >= 2);
    }

    [Fact]
    public async Task Publish_ParallelAggregateAll_WithMaxDegreeOfParallelism_LimitsConcurrency()
    {
        var probe = new ParallelProbe();
        var services = new ServiceCollection();
        services.AddSingleton(probe);
        services.AddSingleton<INotificationHandler<LimitedParallelNotification>, LimitedParallelFirstHandler>();
        services.AddSingleton<INotificationHandler<LimitedParallelNotification>, LimitedParallelSecondHandler>();
        services.AddSingleton<INotificationHandler<LimitedParallelNotification>, LimitedParallelThirdHandler>();
        services.AddSingleton<INotificationHandler<LimitedParallelNotification>, LimitedParallelFourthHandler>();
        services.AddSingleton(new NotificationPublishOptions(
            NotificationPublishMode.ParallelAggregateAll,
            2,
            []));
        services.AddSingleton<IMediator, Mediator>();

        await using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        Task publishTask = mediator.Publish(new LimitedParallelNotification());

        await probe.BothStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
        await publishTask.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.True(probe.MaxConcurrentHandlers >= 2);
        Assert.True(probe.MaxConcurrentHandlers <= 2);
    }

    [Fact]
    public async Task Publish_ParallelAggregateAll_WithMaxDegreeOfParallelismOne_EffectivelyRunsSequentially()
    {
        var probe = new SingleConcurrencyProbe();
        var services = new ServiceCollection();
        services.AddSingleton(probe);
        services.AddSingleton<INotificationHandler<MdopOneNotification>, MdopOneFirstHandler>();
        services.AddSingleton<INotificationHandler<MdopOneNotification>, MdopOneSecondHandler>();
        services.AddSingleton(new NotificationPublishOptions(
            NotificationPublishMode.ParallelAggregateAll,
            1,
            []));
        services.AddSingleton<IMediator, Mediator>();

        await using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        Task publishTask = mediator.Publish(new MdopOneNotification());

        await probe.FirstStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
        Task finished = await Task.WhenAny(probe.SecondStarted.Task, Task.Delay(150));
        Assert.NotSame(probe.SecondStarted.Task, finished);

        probe.Release.TrySetResult();
        await publishTask;

        Assert.Equal(1, probe.MaxConcurrentHandlers);
    }

    [Fact]
    public async Task Publish_ParallelAggregateAll_ThrowsAggregateException_WhenHandlersFail()
    {
        var calls = new List<string>();
        var services = new ServiceCollection();
        services.AddSingleton(calls);
        services.AddSingleton<INotificationHandler<ParallelFaultNotification>, ParallelFaultFirstHandler>();
        services.AddSingleton<INotificationHandler<ParallelFaultNotification>, ParallelFaultSecondHandler>();
        services.AddSingleton(new NotificationPublishOptions(
            NotificationPublishMode.ParallelAggregateAll,
            null,
            []));
        services.AddSingleton<IMediator, Mediator>();

        await using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        AggregateException exception = await Assert.ThrowsAsync<AggregateException>(() => mediator.Publish(new ParallelFaultNotification()));

        Assert.Equal(2, calls.Count);
        Assert.Equal(2, exception.InnerExceptions.Count);
        Assert.Contains(exception.InnerExceptions, static ex => ex is InvalidOperationException and { Message: "parallel-first" });
        Assert.Contains(exception.InnerExceptions, static ex => ex is InvalidOperationException and { Message: "parallel-second" });
    }

    [Fact]
    public async Task Publish_ParallelAggregateAll_AggregatesCancellationExceptions_FromHandlers()
    {
        var services = new ServiceCollection();
        services.AddSingleton<INotificationHandler<CancellableParallelNotification>, CancellableParallelFirstHandler>();
        services.AddSingleton<INotificationHandler<CancellableParallelNotification>, CancellableParallelSecondHandler>();
        services.AddSingleton(new NotificationPublishOptions(
            NotificationPublishMode.ParallelAggregateAll,
            2,
            []));
        services.AddSingleton<IMediator, Mediator>();

        await using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        AggregateException exception = await Assert.ThrowsAsync<AggregateException>(() => mediator.Publish(new CancellableParallelNotification(), cancellation.Token));

        Assert.Equal(2, exception.InnerExceptions.Count);
        Assert.All(exception.InnerExceptions, static ex => Assert.IsAssignableFrom<OperationCanceledException>(ex));
    }

    [Fact]
    public async Task Publish_SequentialAggregateAll_AggregatesCancellationExceptions_AndContinuesHandlers()
    {
        var calls = new List<string>();
        var services = new ServiceCollection();
        services.AddSingleton(calls);
        services.AddSingleton<INotificationHandler<CancellableSequentialNotification>, CancellableSequentialFirstHandler>();
        services.AddSingleton<INotificationHandler<CancellableSequentialNotification>, CancellableSequentialSecondHandler>();
        services.AddSingleton(new NotificationPublishOptions(
            NotificationPublishMode.SequentialAggregateAll,
            null,
            []));
        services.AddSingleton<IMediator, Mediator>();

        await using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        AggregateException exception = await Assert.ThrowsAsync<AggregateException>(() => mediator.Publish(new CancellableSequentialNotification(), cancellation.Token));

        Assert.Equal(["seq-first", "seq-second"], calls);
        Assert.Equal(2, exception.InnerExceptions.Count);
        Assert.All(exception.InnerExceptions, static ex => Assert.IsAssignableFrom<OperationCanceledException>(ex));
    }

    [Fact]
    public async Task Publish_ParallelAggregateAll_MaxDegreeOfParallelism_IsScopedPerPublishCall()
    {
        var probe = new MultiPublishProbe();
        var services = new ServiceCollection();
        services.AddSingleton(probe);
        services.AddSingleton<INotificationHandler<PerPublishLimitNotification>, PerPublishLimitFirstHandler>();
        services.AddSingleton<INotificationHandler<PerPublishLimitNotification>, PerPublishLimitSecondHandler>();
        services.AddSingleton(new NotificationPublishOptions(
            NotificationPublishMode.ParallelAggregateAll,
            1,
            []));
        services.AddSingleton<IMediator, Mediator>();

        await using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        Task publish1 = mediator.Publish(new PerPublishLimitNotification());
        Task publish2 = mediator.Publish(new PerPublishLimitNotification());

        try
        {
            await probe.TwoStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
        }
        finally
        {
            probe.Release.TrySetResult();
        }

        await Task.WhenAll(publish1, publish2);
        Assert.True(probe.MaxConcurrentHandlers >= 2);
    }

    [Fact]
    public async Task Publish_SequentialAggregateAll_ThrowsAggregateException_WithMixedCancellationAndFaults()
    {
        var calls = new List<string>();
        var services = new ServiceCollection();
        services.AddSingleton(calls);
        services.AddSingleton<INotificationHandler<MixedSequentialFaultNotification>, MixedSequentialCancelHandler>();
        services.AddSingleton<INotificationHandler<MixedSequentialFaultNotification>, MixedSequentialFaultHandler>();
        services.AddSingleton(new NotificationPublishOptions(
            NotificationPublishMode.SequentialAggregateAll,
            null,
            []));
        services.AddSingleton<IMediator, Mediator>();

        await using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        AggregateException exception = await Assert.ThrowsAsync<AggregateException>(
            () => mediator.Publish(new MixedSequentialFaultNotification(), cancellation.Token));

        Assert.Equal(["mixed-cancel", "mixed-fault"], calls);
        Assert.Equal(2, exception.InnerExceptions.Count);
        Assert.Contains(exception.InnerExceptions, static ex => ex is OperationCanceledException);
        Assert.Contains(exception.InnerExceptions, static ex => ex is InvalidOperationException and { Message: "mixed-fault" });
    }

    [Fact]
    public async Task Publish_SequentialAggregateAll_CollectsOnlyFailingHandlers()
    {
        var calls = new List<string>();
        var services = new ServiceCollection();
        services.AddSingleton(calls);
        services.AddSingleton<INotificationHandler<PartialFailureNotification>, PartialFailureFirstHandler>();
        services.AddSingleton<INotificationHandler<PartialFailureNotification>, PartialFailureFailingHandler>();
        services.AddSingleton<INotificationHandler<PartialFailureNotification>, PartialFailureThirdHandler>();
        services.AddSingleton(new NotificationPublishOptions(
            NotificationPublishMode.SequentialAggregateAll,
            null,
            []));
        services.AddSingleton<IMediator, Mediator>();

        await using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        AggregateException exception = await Assert.ThrowsAsync<AggregateException>(() => mediator.Publish(new PartialFailureNotification()));

        Assert.Equal(["partial-first", "partial-fail", "partial-third"], calls);
        Assert.Single(exception.InnerExceptions);
        Assert.Contains(exception.InnerExceptions, static ex => ex is InvalidOperationException and { Message: "partial-fail" });
    }

    [Fact]
    public async Task Publish_InvalidMode_ThrowsInvalidOperationException()
    {
        var services = new ServiceCollection();
        services.AddSingleton<INotificationHandler<NoHandlerNotification>, NoHandlerNotificationHandler>();
        services.AddSingleton(new NotificationPublishOptions(
            (NotificationPublishMode)int.MaxValue,
            null,
            []));
        services.AddSingleton<IMediator, Mediator>();

        await using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(() => mediator.Publish(new NoHandlerNotification()));

        Assert.Contains("Unsupported notification publish mode", exception.Message);
    }

    [Fact]
    public async Task Publish_AddMediora_DefaultSequentialFailFast_StopsOnFirstFailure()
    {
        var calls = new List<string>();
        var services = new ServiceCollection();
        services.AddSingleton(calls);
        services.AddMediora(options =>
        {
            options.RegisterServicesFromAssembly(typeof(MediatorPublishTests).Assembly);
            options.DefaultNotificationPublishMode = NotificationPublishMode.SequentialFailFast;
        });

        await using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(() => mediator.Publish(new AddMedioraFailFastNotification()));

        Assert.Single(calls);
        Assert.True(calls[0] is "am-ff-first" or "am-ff-second");
        Assert.True(exception.Message is "am-ff-first" or "am-ff-second");
    }

    [Fact]
    public async Task Publish_AddMediora_PerTypeOverride_SequentialAggregateAll_AggregatesFailures()
    {
        var calls = new List<string>();
        var services = new ServiceCollection();
        services.AddSingleton(calls);
        services.AddMediora(options =>
        {
            options.RegisterServicesFromAssembly(typeof(MediatorPublishTests).Assembly);
            options.DefaultNotificationPublishMode = NotificationPublishMode.SequentialFailFast;
            options.ConfigureNotificationPublishMode<AddMedioraSequentialAggregateNotification>(NotificationPublishMode.SequentialAggregateAll);
        });

        await using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        AggregateException exception = await Assert.ThrowsAsync<AggregateException>(() => mediator.Publish(new AddMedioraSequentialAggregateNotification()));

        Assert.Equal(3, calls.Count);
        Assert.Contains("am-sa-success", calls);
        Assert.Contains("am-sa-fail-1", calls);
        Assert.Contains("am-sa-fail-2", calls);
        Assert.Equal(2, exception.InnerExceptions.Count);
        Assert.Contains(exception.InnerExceptions, static ex => ex is InvalidOperationException and { Message: "am-sa-fail-1" });
        Assert.Contains(exception.InnerExceptions, static ex => ex is InvalidOperationException and { Message: "am-sa-fail-2" });
    }

    [Fact]
    public async Task Publish_AddMediora_PerTypeOverride_ParallelAggregateAll_RunsConcurrently()
    {
        var probe = new ParallelProbe();
        var services = new ServiceCollection();
        services.AddSingleton(probe);
        services.AddMediora(options =>
        {
            options.RegisterServicesFromAssembly(typeof(MediatorPublishTests).Assembly);
            options.DefaultNotificationPublishMode = NotificationPublishMode.SequentialFailFast;
            options.ConfigureNotificationPublishMode<AddMedioraParallelNotification>(NotificationPublishMode.ParallelAggregateAll);
        });

        await using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        Task publishTask = mediator.Publish(new AddMedioraParallelNotification());

        try
        {
            await probe.BothStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
        }
        finally
        {
            probe.Release.TrySetResult();
        }

        await publishTask;
        Assert.True(probe.MaxConcurrentHandlers >= 2);
    }

    [Fact]
    public async Task Publish_AddMediora_ParallelAggregateAll_RespectsMaxDegreeOfParallelism()
    {
        var probe = new ParallelProbe();
        var services = new ServiceCollection();
        services.AddSingleton(probe);
        services.AddMediora(options =>
        {
            options.RegisterServicesFromAssembly(typeof(MediatorPublishTests).Assembly);
            options.DefaultNotificationPublishMode = NotificationPublishMode.SequentialFailFast;
            options.NotificationParallelMaxDegreeOfParallelism = 2;
            options.ConfigureNotificationPublishMode<AddMedioraLimitedParallelNotification>(NotificationPublishMode.ParallelAggregateAll);
        });

        await using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        Task publishTask = mediator.Publish(new AddMedioraLimitedParallelNotification());

        await probe.BothStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
        await publishTask.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.True(probe.MaxConcurrentHandlers >= 2);
        Assert.True(probe.MaxConcurrentHandlers <= 2);
    }

    [Fact]
    public async Task Publish_AddMediora_RuntimeOverload_UsesRuntimeTypeOverride()
    {
        var calls = new List<string>();
        var services = new ServiceCollection();
        services.AddSingleton(calls);
        services.AddMediora(options =>
        {
            options.RegisterServicesFromAssembly(typeof(MediatorPublishTests).Assembly);
            options.DefaultNotificationPublishMode = NotificationPublishMode.ParallelAggregateAll;
            options.ConfigureNotificationPublishMode<AddMedioraRuntimeOverrideNotification>(NotificationPublishMode.SequentialFailFast);
        });

        await using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        INotification notification = new AddMedioraRuntimeOverrideNotification();
        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(() => mediator.Publish(notification));

        Assert.Single(calls);
        Assert.True(calls[0] is "am-ro-first" or "am-ro-second");
        Assert.True(exception.Message is "am-ro-first" or "am-ro-second");
    }

    [Fact]
    public async Task Publish_Stress_SequentialFailFast_IsStable_UnderConcurrentPublishes()
    {
        const int publishCount = 120;
        var counter = new PublishStressCounter();
        var services = new ServiceCollection();
        services.AddSingleton(counter);
        services.AddSingleton<INotificationHandler<StressFailFastNotification>, StressFailFastFirstHandler>();
        services.AddSingleton<INotificationHandler<StressFailFastNotification>, StressFailFastSecondHandler>();
        services.AddSingleton(new NotificationPublishOptions(
            NotificationPublishMode.SequentialFailFast,
            null,
            []));
        services.AddSingleton<IMediator, Mediator>();

        await using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        bool[] results = await Task.WhenAll(Enumerable.Range(0, publishCount).Select(async _ =>
        {
            try
            {
                await mediator.Publish(new StressFailFastNotification());
                return false;
            }
            catch (InvalidOperationException exception)
            {
                return exception.Message == "stress-ff";
            }
        }));

        Assert.All(results, static result => Assert.True(result));
        Assert.Equal(publishCount, counter.FailFastFirstCalls);
        Assert.Equal(0, counter.FailFastSecondCalls);
    }

    [Fact]
    public async Task Publish_Stress_SequentialAggregateAll_IsStable_UnderConcurrentPublishes()
    {
        const int publishCount = 100;
        var counter = new PublishStressCounter();
        var services = new ServiceCollection();
        services.AddSingleton(counter);
        services.AddSingleton<INotificationHandler<StressAggregateNotification>, StressAggregateSuccessHandler>();
        services.AddSingleton<INotificationHandler<StressAggregateNotification>, StressAggregateFirstFailHandler>();
        services.AddSingleton<INotificationHandler<StressAggregateNotification>, StressAggregateSecondFailHandler>();
        services.AddSingleton(new NotificationPublishOptions(
            NotificationPublishMode.SequentialAggregateAll,
            null,
            []));
        services.AddSingleton<IMediator, Mediator>();

        await using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        bool[] results = await Task.WhenAll(Enumerable.Range(0, publishCount).Select(async _ =>
        {
            try
            {
                await mediator.Publish(new StressAggregateNotification());
                return false;
            }
            catch (AggregateException exception)
            {
                return exception.InnerExceptions.Count == 2;
            }
        }));

        Assert.All(results, static result => Assert.True(result));
        Assert.Equal(publishCount, counter.AggregateSuccessCalls);
        Assert.Equal(publishCount, counter.AggregateFailFirstCalls);
        Assert.Equal(publishCount, counter.AggregateFailSecondCalls);
    }

    [Fact]
    public async Task Publish_Stress_ParallelAggregateAll_IsStable_UnderConcurrentPublishes()
    {
        const int publishCount = 100;
        var counter = new PublishStressCounter();
        var services = new ServiceCollection();
        services.AddSingleton(counter);
        services.AddSingleton<INotificationHandler<StressParallelNotification>, StressParallelFirstHandler>();
        services.AddSingleton<INotificationHandler<StressParallelNotification>, StressParallelSecondHandler>();
        services.AddSingleton<INotificationHandler<StressParallelNotification>, StressParallelThirdHandler>();
        services.AddSingleton(new NotificationPublishOptions(
            NotificationPublishMode.ParallelAggregateAll,
            4,
            []));
        services.AddSingleton<IMediator, Mediator>();

        await using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        await Task.WhenAll(Enumerable.Range(0, publishCount).Select(_ => mediator.Publish(new StressParallelNotification())));

        Assert.Equal(publishCount * 3, counter.ParallelCalls);
        Assert.True(counter.ParallelMaxConcurrentCalls >= 2);
    }

    [Fact]
    public async Task Publish_ConcurrentMixedOverrides_ApplyCorrectModePerNotificationType()
    {
        const int perTypePublishes = 40;
        var counter = new MixedOverrideCounter();
        var services = new ServiceCollection();
        services.AddSingleton(counter);
        services.AddSingleton<INotificationHandler<MixedOverrideFailFastNotification>, MixedOverrideFailFastFirstHandler>();
        services.AddSingleton<INotificationHandler<MixedOverrideFailFastNotification>, MixedOverrideFailFastSecondHandler>();
        services.AddSingleton<INotificationHandler<MixedOverrideAggregateNotification>, MixedOverrideAggregateSuccessHandler>();
        services.AddSingleton<INotificationHandler<MixedOverrideAggregateNotification>, MixedOverrideAggregateFailFirstHandler>();
        services.AddSingleton<INotificationHandler<MixedOverrideAggregateNotification>, MixedOverrideAggregateFailSecondHandler>();
        services.AddSingleton<INotificationHandler<MixedOverrideParallelNotification>, MixedOverrideParallelFirstHandler>();
        services.AddSingleton<INotificationHandler<MixedOverrideParallelNotification>, MixedOverrideParallelSecondHandler>();
        services.AddSingleton(new NotificationPublishOptions(
            NotificationPublishMode.SequentialFailFast,
            4,
            new Dictionary<Type, NotificationPublishMode>
            {
                [typeof(MixedOverrideAggregateNotification)] = NotificationPublishMode.SequentialAggregateAll,
                [typeof(MixedOverrideParallelNotification)] = NotificationPublishMode.ParallelAggregateAll
            }));
        services.AddSingleton<IMediator, Mediator>();

        await using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        Task[] failFastTasks = Enumerable.Range(0, perTypePublishes).Select(async _ =>
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => mediator.Publish(new MixedOverrideFailFastNotification()));
        }).ToArray();

        Task[] aggregateTasks = Enumerable.Range(0, perTypePublishes).Select(async _ =>
        {
            await Assert.ThrowsAsync<AggregateException>(() => mediator.Publish(new MixedOverrideAggregateNotification()));
        }).ToArray();

        Task[] parallelTasks = Enumerable.Range(0, perTypePublishes).Select(_ => mediator.Publish(new MixedOverrideParallelNotification())).ToArray();

        await Task.WhenAll([.. failFastTasks, .. aggregateTasks, .. parallelTasks]);

        Assert.Equal(perTypePublishes, counter.FailFastFirstCalls);
        Assert.Equal(0, counter.FailFastSecondCalls);

        Assert.Equal(perTypePublishes, counter.AggregateSuccessCalls);
        Assert.Equal(perTypePublishes, counter.AggregateFailFirstCalls);
        Assert.Equal(perTypePublishes, counter.AggregateFailSecondCalls);

        Assert.Equal(perTypePublishes * 2, counter.ParallelCalls);
        Assert.True(counter.ParallelMaxConcurrentCalls >= 2);
    }

    [Fact]
    public async Task Publish_ParallelAggregateAll_MaxDegreeIsScopedPerCall_UnderHighContention()
    {
        const int publishCount = 25;
        var probe = new ScopedParallelProbe();
        var services = new ServiceCollection();
        services.AddSingleton(probe);
        services.AddSingleton<INotificationHandler<ScopedParallelNotification>, ScopedParallelFirstHandler>();
        services.AddSingleton<INotificationHandler<ScopedParallelNotification>, ScopedParallelSecondHandler>();
        services.AddSingleton(new NotificationPublishOptions(
            NotificationPublishMode.ParallelAggregateAll,
            1,
            []));
        services.AddSingleton<IMediator, Mediator>();

        await using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        await Task.WhenAll(Enumerable.Range(1, publishCount).Select(id => mediator.Publish(new ScopedParallelNotification(id))));

        Assert.True(probe.GlobalMaxConcurrentCalls >= 2);
        Assert.All(probe.MaxByPublishId.Values, static max => Assert.True(max <= 1));
    }

    [Fact]
    public async Task Publish_ParallelAggregateAll_RepeatedRuns_DoNotFlake()
    {
        const int runs = 10;
        var services = new ServiceCollection();
        services.AddSingleton<INotificationHandler<RepeatParallelNotification>, RepeatParallelFirstHandler>();
        services.AddSingleton<INotificationHandler<RepeatParallelNotification>, RepeatParallelSecondHandler>();
        services.AddSingleton(new NotificationPublishOptions(
            NotificationPublishMode.ParallelAggregateAll,
            2,
            []));
        services.AddSingleton<IMediator, Mediator>();

        await using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        for (int run = 0; run < runs; run++)
        {
            await mediator.Publish(new RepeatParallelNotification());
        }
    }

    [Fact]
    public async Task Publish_ParallelAggregateAll_WithMaxDegreeOfParallelismOne_StillAggregatesAllHandlerFailures()
    {
        var services = new ServiceCollection();
        services.AddSingleton<INotificationHandler<MdopOneFaultNotification>, MdopOneFaultFirstHandler>();
        services.AddSingleton<INotificationHandler<MdopOneFaultNotification>, MdopOneFaultSecondHandler>();
        services.AddSingleton(new NotificationPublishOptions(
            NotificationPublishMode.ParallelAggregateAll,
            1,
            []));
        services.AddSingleton<IMediator, Mediator>();

        await using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        AggregateException exception = await Assert.ThrowsAsync<AggregateException>(() => mediator.Publish(new MdopOneFaultNotification()));

        Assert.Equal(2, exception.InnerExceptions.Count);
        Assert.Contains(exception.InnerExceptions, static ex => ex is InvalidOperationException and { Message: "mdop1-first" });
        Assert.Contains(exception.InnerExceptions, static ex => ex is InvalidOperationException and { Message: "mdop1-second" });
    }

    private sealed record OrderPlacedNotification : INotification;

    private sealed class FirstNotificationHandler : INotificationHandler<OrderPlacedNotification>
    {
        private readonly List<string> _calls;

        public FirstNotificationHandler(List<string> calls)
        {
            _calls = calls;
        }

        public Task Handle(OrderPlacedNotification notification, CancellationToken cancellationToken)
        {
            _calls.Add("first");
            return Task.CompletedTask;
        }
    }

    private sealed class SecondNotificationHandler : INotificationHandler<OrderPlacedNotification>
    {
        private readonly List<string> _calls;

        public SecondNotificationHandler(List<string> calls)
        {
            _calls = calls;
        }

        public Task Handle(OrderPlacedNotification notification, CancellationToken cancellationToken)
        {
            _calls.Add("second");
            return Task.CompletedTask;
        }
    }

    private sealed record NoHandlerNotification : INotification;

    private record BaseExactNotification : INotification;

    private sealed record DerivedExactNotification : BaseExactNotification;

    private sealed record RuntimeOverrideNotification : INotification;

    private sealed record PartialFailureNotification : INotification;

    private sealed record AddMedioraFailFastNotification : INotification;

    private sealed record AddMedioraSequentialAggregateNotification : INotification;

    private sealed record AddMedioraParallelNotification : INotification;

    private sealed record AddMedioraLimitedParallelNotification : INotification;

    private sealed record AddMedioraRuntimeOverrideNotification : INotification;

    private sealed record StressFailFastNotification : INotification;

    private sealed record StressAggregateNotification : INotification;

    private sealed record StressParallelNotification : INotification;

    private sealed record MixedOverrideFailFastNotification : INotification;

    private sealed record MixedOverrideAggregateNotification : INotification;

    private sealed record MixedOverrideParallelNotification : INotification;

    private sealed record ScopedParallelNotification(int Id) : INotification;

    private sealed record RepeatParallelNotification : INotification;

    private sealed record OrderedNotification : INotification;

    private sealed class OrderedFirstHandler : INotificationHandler<OrderedNotification>
    {
        private readonly List<string> _calls;

        public OrderedFirstHandler(List<string> calls)
        {
            _calls = calls;
        }

        public Task Handle(OrderedNotification notification, CancellationToken cancellationToken)
        {
            _calls.Add("1");
            return Task.CompletedTask;
        }
    }

    private sealed class NoHandlerNotificationHandler : INotificationHandler<NoHandlerNotification>
    {
        public Task Handle(NoHandlerNotification notification, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class DerivedExactFirstHandler : INotificationHandler<DerivedExactNotification>
    {
        private readonly SingleConcurrencyProbe _probe;

        public DerivedExactFirstHandler(SingleConcurrencyProbe probe)
        {
            _probe = probe;
        }

        public async Task Handle(DerivedExactNotification notification, CancellationToken cancellationToken)
        {
            _probe.Enter();
            try
            {
                await _probe.Release.Task.WaitAsync(cancellationToken);
            }
            finally
            {
                _probe.Exit();
            }
        }
    }

    private sealed class DerivedExactSecondHandler : INotificationHandler<DerivedExactNotification>
    {
        private readonly SingleConcurrencyProbe _probe;

        public DerivedExactSecondHandler(SingleConcurrencyProbe probe)
        {
            _probe = probe;
        }

        public async Task Handle(DerivedExactNotification notification, CancellationToken cancellationToken)
        {
            _probe.Enter();
            try
            {
                await _probe.Release.Task.WaitAsync(cancellationToken);
            }
            finally
            {
                _probe.Exit();
            }
        }
    }

    private sealed class RuntimeOverrideFirstHandler : INotificationHandler<RuntimeOverrideNotification>
    {
        private readonly List<string> _calls;

        public RuntimeOverrideFirstHandler(List<string> calls)
        {
            _calls = calls;
        }

        public Task Handle(RuntimeOverrideNotification notification, CancellationToken cancellationToken)
        {
            _calls.Add("runtime-first");
            throw new InvalidOperationException("runtime-first-fail");
        }
    }

    private sealed class RuntimeOverrideSecondHandler : INotificationHandler<RuntimeOverrideNotification>
    {
        private readonly List<string> _calls;

        public RuntimeOverrideSecondHandler(List<string> calls)
        {
            _calls = calls;
        }

        public Task Handle(RuntimeOverrideNotification notification, CancellationToken cancellationToken)
        {
            _calls.Add("runtime-second");
            return Task.CompletedTask;
        }
    }

    private sealed class PartialFailureFirstHandler : INotificationHandler<PartialFailureNotification>
    {
        private readonly List<string> _calls;

        public PartialFailureFirstHandler(List<string> calls)
        {
            _calls = calls;
        }

        public Task Handle(PartialFailureNotification notification, CancellationToken cancellationToken)
        {
            _calls.Add("partial-first");
            return Task.CompletedTask;
        }
    }

    private sealed class PartialFailureFailingHandler : INotificationHandler<PartialFailureNotification>
    {
        private readonly List<string> _calls;

        public PartialFailureFailingHandler(List<string> calls)
        {
            _calls = calls;
        }

        public Task Handle(PartialFailureNotification notification, CancellationToken cancellationToken)
        {
            _calls.Add("partial-fail");
            throw new InvalidOperationException("partial-fail");
        }
    }

    private sealed class PartialFailureThirdHandler : INotificationHandler<PartialFailureNotification>
    {
        private readonly List<string> _calls;

        public PartialFailureThirdHandler(List<string> calls)
        {
            _calls = calls;
        }

        public Task Handle(PartialFailureNotification notification, CancellationToken cancellationToken)
        {
            _calls.Add("partial-third");
            return Task.CompletedTask;
        }
    }

    private sealed class AddMedioraFailFastFirstHandler : INotificationHandler<AddMedioraFailFastNotification>
    {
        private readonly List<string> _calls;

        public AddMedioraFailFastFirstHandler(List<string> calls)
        {
            _calls = calls;
        }

        public Task Handle(AddMedioraFailFastNotification notification, CancellationToken cancellationToken)
        {
            _calls.Add("am-ff-first");
            throw new InvalidOperationException("am-ff-first");
        }
    }

    private sealed class AddMedioraFailFastSecondHandler : INotificationHandler<AddMedioraFailFastNotification>
    {
        private readonly List<string> _calls;

        public AddMedioraFailFastSecondHandler(List<string> calls)
        {
            _calls = calls;
        }

        public Task Handle(AddMedioraFailFastNotification notification, CancellationToken cancellationToken)
        {
            _calls.Add("am-ff-second");
            throw new InvalidOperationException("am-ff-second");
        }
    }

    private sealed class AddMedioraSequentialAggregateSuccessHandler : INotificationHandler<AddMedioraSequentialAggregateNotification>
    {
        private readonly List<string> _calls;

        public AddMedioraSequentialAggregateSuccessHandler(List<string> calls)
        {
            _calls = calls;
        }

        public Task Handle(AddMedioraSequentialAggregateNotification notification, CancellationToken cancellationToken)
        {
            _calls.Add("am-sa-success");
            return Task.CompletedTask;
        }
    }

    private sealed class AddMedioraSequentialAggregateFailFirstHandler : INotificationHandler<AddMedioraSequentialAggregateNotification>
    {
        private readonly List<string> _calls;

        public AddMedioraSequentialAggregateFailFirstHandler(List<string> calls)
        {
            _calls = calls;
        }

        public Task Handle(AddMedioraSequentialAggregateNotification notification, CancellationToken cancellationToken)
        {
            _calls.Add("am-sa-fail-1");
            throw new InvalidOperationException("am-sa-fail-1");
        }
    }

    private sealed class AddMedioraSequentialAggregateFailSecondHandler : INotificationHandler<AddMedioraSequentialAggregateNotification>
    {
        private readonly List<string> _calls;

        public AddMedioraSequentialAggregateFailSecondHandler(List<string> calls)
        {
            _calls = calls;
        }

        public Task Handle(AddMedioraSequentialAggregateNotification notification, CancellationToken cancellationToken)
        {
            _calls.Add("am-sa-fail-2");
            throw new InvalidOperationException("am-sa-fail-2");
        }
    }

    private sealed class AddMedioraParallelFirstHandler : INotificationHandler<AddMedioraParallelNotification>
    {
        private readonly ParallelProbe _probe;

        public AddMedioraParallelFirstHandler(ParallelProbe probe)
        {
            _probe = probe;
        }

        public async Task Handle(AddMedioraParallelNotification notification, CancellationToken cancellationToken)
        {
            _probe.Enter();
            try
            {
                await _probe.Release.Task.WaitAsync(cancellationToken);
            }
            finally
            {
                _probe.Exit();
            }
        }
    }

    private sealed class AddMedioraParallelSecondHandler : INotificationHandler<AddMedioraParallelNotification>
    {
        private readonly ParallelProbe _probe;

        public AddMedioraParallelSecondHandler(ParallelProbe probe)
        {
            _probe = probe;
        }

        public async Task Handle(AddMedioraParallelNotification notification, CancellationToken cancellationToken)
        {
            _probe.Enter();
            try
            {
                await _probe.Release.Task.WaitAsync(cancellationToken);
            }
            finally
            {
                _probe.Exit();
            }
        }
    }

    private sealed class AddMedioraLimitedParallelFirstHandler : INotificationHandler<AddMedioraLimitedParallelNotification>
    {
        private readonly ParallelProbe _probe;

        public AddMedioraLimitedParallelFirstHandler(ParallelProbe probe)
        {
            _probe = probe;
        }

        public async Task Handle(AddMedioraLimitedParallelNotification notification, CancellationToken cancellationToken)
        {
            await Execute(cancellationToken);
        }

        private async Task Execute(CancellationToken cancellationToken)
        {
            _probe.Enter();
            try
            {
                await Task.Delay(120, cancellationToken);
            }
            finally
            {
                _probe.Exit();
            }
        }
    }

    private sealed class AddMedioraLimitedParallelSecondHandler : INotificationHandler<AddMedioraLimitedParallelNotification>
    {
        private readonly ParallelProbe _probe;

        public AddMedioraLimitedParallelSecondHandler(ParallelProbe probe)
        {
            _probe = probe;
        }

        public async Task Handle(AddMedioraLimitedParallelNotification notification, CancellationToken cancellationToken)
        {
            await Execute(cancellationToken);
        }

        private async Task Execute(CancellationToken cancellationToken)
        {
            _probe.Enter();
            try
            {
                await Task.Delay(120, cancellationToken);
            }
            finally
            {
                _probe.Exit();
            }
        }
    }

    private sealed class AddMedioraLimitedParallelThirdHandler : INotificationHandler<AddMedioraLimitedParallelNotification>
    {
        private readonly ParallelProbe _probe;

        public AddMedioraLimitedParallelThirdHandler(ParallelProbe probe)
        {
            _probe = probe;
        }

        public async Task Handle(AddMedioraLimitedParallelNotification notification, CancellationToken cancellationToken)
        {
            await Execute(cancellationToken);
        }

        private async Task Execute(CancellationToken cancellationToken)
        {
            _probe.Enter();
            try
            {
                await Task.Delay(120, cancellationToken);
            }
            finally
            {
                _probe.Exit();
            }
        }
    }

    private sealed class AddMedioraLimitedParallelFourthHandler : INotificationHandler<AddMedioraLimitedParallelNotification>
    {
        private readonly ParallelProbe _probe;

        public AddMedioraLimitedParallelFourthHandler(ParallelProbe probe)
        {
            _probe = probe;
        }

        public async Task Handle(AddMedioraLimitedParallelNotification notification, CancellationToken cancellationToken)
        {
            await Execute(cancellationToken);
        }

        private async Task Execute(CancellationToken cancellationToken)
        {
            _probe.Enter();
            try
            {
                await Task.Delay(120, cancellationToken);
            }
            finally
            {
                _probe.Exit();
            }
        }
    }

    private sealed class AddMedioraRuntimeOverrideFirstHandler : INotificationHandler<AddMedioraRuntimeOverrideNotification>
    {
        private readonly List<string> _calls;

        public AddMedioraRuntimeOverrideFirstHandler(List<string> calls)
        {
            _calls = calls;
        }

        public Task Handle(AddMedioraRuntimeOverrideNotification notification, CancellationToken cancellationToken)
        {
            _calls.Add("am-ro-first");
            throw new InvalidOperationException("am-ro-first");
        }
    }

    private sealed class AddMedioraRuntimeOverrideSecondHandler : INotificationHandler<AddMedioraRuntimeOverrideNotification>
    {
        private readonly List<string> _calls;

        public AddMedioraRuntimeOverrideSecondHandler(List<string> calls)
        {
            _calls = calls;
        }

        public Task Handle(AddMedioraRuntimeOverrideNotification notification, CancellationToken cancellationToken)
        {
            _calls.Add("am-ro-second");
            throw new InvalidOperationException("am-ro-second");
        }
    }

    private sealed class OrderedSecondHandler : INotificationHandler<OrderedNotification>
    {
        private readonly List<string> _calls;

        public OrderedSecondHandler(List<string> calls)
        {
            _calls = calls;
        }

        public Task Handle(OrderedNotification notification, CancellationToken cancellationToken)
        {
            _calls.Add("2");
            return Task.CompletedTask;
        }
    }

    private sealed record FaultyNotification : INotification;

    private sealed class FirstFailingHandler : INotificationHandler<FaultyNotification>
    {
        private readonly List<string> _calls;

        public FirstFailingHandler(List<string> calls)
        {
            _calls = calls;
        }

        public Task Handle(FaultyNotification notification, CancellationToken cancellationToken)
        {
            _calls.Add("first");
            throw new InvalidOperationException("first");
        }
    }

    private sealed class SecondFailingHandler : INotificationHandler<FaultyNotification>
    {
        private readonly List<string> _calls;

        public SecondFailingHandler(List<string> calls)
        {
            _calls = calls;
        }

        public Task Handle(FaultyNotification notification, CancellationToken cancellationToken)
        {
            _calls.Add("second");
            throw new InvalidOperationException("second");
        }
    }

    private sealed record TypedNotification : INotification;

    private sealed record RuntimeTypedNotification : INotification;

    private sealed record ParallelNotification : INotification;

    private sealed record MixedSequentialNotification : INotification;

    private sealed record MixedParallelNotification : INotification;

    private sealed record DefaultParallelNotification : INotification;

    private sealed record LimitedParallelNotification : INotification;

    private sealed record MdopOneNotification : INotification;

    private sealed record ParallelFaultNotification : INotification;

    private sealed record CancellableParallelNotification : INotification;

    private sealed record CancellableSequentialNotification : INotification;

    private sealed record PerPublishLimitNotification : INotification;

    private sealed record MixedSequentialFaultNotification : INotification;

    private sealed record MdopOneFaultNotification : INotification;

    private sealed class TypedNotificationHandler : INotificationHandler<TypedNotification>
    {
        private readonly List<string> _calls;

        public TypedNotificationHandler(List<string> calls)
        {
            _calls = calls;
        }

        public Task Handle(TypedNotification notification, CancellationToken cancellationToken)
        {
            _calls.Add("typed");
            return Task.CompletedTask;
        }
    }

    private sealed class RuntimeTypedNotificationHandler : INotificationHandler<RuntimeTypedNotification>
    {
        private readonly List<string> _calls;

        public RuntimeTypedNotificationHandler(List<string> calls)
        {
            _calls = calls;
        }

        public Task Handle(RuntimeTypedNotification notification, CancellationToken cancellationToken)
        {
            _calls.Add("runtime-typed");
            return Task.CompletedTask;
        }
    }

    private sealed class ParallelProbe
    {
        private int _currentConcurrentHandlers;
        private int _maxConcurrentHandlers;
        private int _startedCount;

        public TaskCompletionSource BothStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int MaxConcurrentHandlers => _maxConcurrentHandlers;

        public void Enter()
        {
            int current = Interlocked.Increment(ref _currentConcurrentHandlers);
            InterlockedExtensions.Max(ref _maxConcurrentHandlers, current);
            if (Interlocked.Increment(ref _startedCount) == 2)
            {
                BothStarted.TrySetResult();
            }
        }

        public void Exit()
        {
            Interlocked.Decrement(ref _currentConcurrentHandlers);
        }
    }

    private sealed class ParallelFirstHandler : INotificationHandler<ParallelNotification>, INotificationHandler<MixedParallelNotification>
    {
        private readonly ParallelProbe _probe;

        public ParallelFirstHandler(ParallelProbe probe)
        {
            _probe = probe;
        }

        public async Task Handle(ParallelNotification notification, CancellationToken cancellationToken)
        {
            await Execute(cancellationToken);
        }

        public async Task Handle(MixedParallelNotification notification, CancellationToken cancellationToken)
        {
            await Execute(cancellationToken);
        }

        private async Task Execute(CancellationToken cancellationToken)
        {
            _probe.Enter();
            try
            {
                await _probe.Release.Task.WaitAsync(cancellationToken);
            }
            finally
            {
                _probe.Exit();
            }
        }
    }

    private sealed class ParallelSecondHandler : INotificationHandler<ParallelNotification>, INotificationHandler<MixedParallelNotification>
    {
        private readonly ParallelProbe _probe;

        public ParallelSecondHandler(ParallelProbe probe)
        {
            _probe = probe;
        }

        public async Task Handle(ParallelNotification notification, CancellationToken cancellationToken)
        {
            await Execute(cancellationToken);
        }

        public async Task Handle(MixedParallelNotification notification, CancellationToken cancellationToken)
        {
            await Execute(cancellationToken);
        }

        private async Task Execute(CancellationToken cancellationToken)
        {
            _probe.Enter();
            try
            {
                await _probe.Release.Task.WaitAsync(cancellationToken);
            }
            finally
            {
                _probe.Exit();
            }
        }
    }

    private sealed class DefaultParallelFirstHandler : INotificationHandler<DefaultParallelNotification>
    {
        private readonly ParallelProbe _probe;

        public DefaultParallelFirstHandler(ParallelProbe probe)
        {
            _probe = probe;
        }

        public async Task Handle(DefaultParallelNotification notification, CancellationToken cancellationToken)
        {
            _probe.Enter();
            try
            {
                await _probe.Release.Task.WaitAsync(cancellationToken);
            }
            finally
            {
                _probe.Exit();
            }
        }
    }

    private sealed class DefaultParallelSecondHandler : INotificationHandler<DefaultParallelNotification>
    {
        private readonly ParallelProbe _probe;

        public DefaultParallelSecondHandler(ParallelProbe probe)
        {
            _probe = probe;
        }

        public async Task Handle(DefaultParallelNotification notification, CancellationToken cancellationToken)
        {
            _probe.Enter();
            try
            {
                await _probe.Release.Task.WaitAsync(cancellationToken);
            }
            finally
            {
                _probe.Exit();
            }
        }
    }

    private sealed class LimitedParallelFirstHandler : INotificationHandler<LimitedParallelNotification>
    {
        private readonly ParallelProbe _probe;

        public LimitedParallelFirstHandler(ParallelProbe probe)
        {
            _probe = probe;
        }

        public async Task Handle(LimitedParallelNotification notification, CancellationToken cancellationToken)
        {
            await Execute(cancellationToken);
        }

        private async Task Execute(CancellationToken cancellationToken)
        {
            _probe.Enter();
            try
            {
                await Task.Delay(120, cancellationToken);
            }
            finally
            {
                _probe.Exit();
            }
        }
    }

    private sealed class LimitedParallelSecondHandler : INotificationHandler<LimitedParallelNotification>
    {
        private readonly ParallelProbe _probe;

        public LimitedParallelSecondHandler(ParallelProbe probe)
        {
            _probe = probe;
        }

        public async Task Handle(LimitedParallelNotification notification, CancellationToken cancellationToken)
        {
            await Execute(cancellationToken);
        }

        private async Task Execute(CancellationToken cancellationToken)
        {
            _probe.Enter();
            try
            {
                await Task.Delay(120, cancellationToken);
            }
            finally
            {
                _probe.Exit();
            }
        }
    }

    private sealed class LimitedParallelThirdHandler : INotificationHandler<LimitedParallelNotification>
    {
        private readonly ParallelProbe _probe;

        public LimitedParallelThirdHandler(ParallelProbe probe)
        {
            _probe = probe;
        }

        public async Task Handle(LimitedParallelNotification notification, CancellationToken cancellationToken)
        {
            await Execute(cancellationToken);
        }

        private async Task Execute(CancellationToken cancellationToken)
        {
            _probe.Enter();
            try
            {
                await Task.Delay(120, cancellationToken);
            }
            finally
            {
                _probe.Exit();
            }
        }
    }

    private sealed class LimitedParallelFourthHandler : INotificationHandler<LimitedParallelNotification>
    {
        private readonly ParallelProbe _probe;

        public LimitedParallelFourthHandler(ParallelProbe probe)
        {
            _probe = probe;
        }

        public async Task Handle(LimitedParallelNotification notification, CancellationToken cancellationToken)
        {
            await Execute(cancellationToken);
        }

        private async Task Execute(CancellationToken cancellationToken)
        {
            _probe.Enter();
            try
            {
                await Task.Delay(120, cancellationToken);
            }
            finally
            {
                _probe.Exit();
            }
        }
    }

    private sealed class SingleConcurrencyProbe
    {
        private int _started;
        private int _currentConcurrentHandlers;
        private int _maxConcurrentHandlers;

        public TaskCompletionSource FirstStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource SecondStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int MaxConcurrentHandlers => _maxConcurrentHandlers;

        public void Enter()
        {
            int started = Interlocked.Increment(ref _started);
            if (started == 1)
            {
                FirstStarted.TrySetResult();
            }
            else if (started == 2)
            {
                SecondStarted.TrySetResult();
            }

            int current = Interlocked.Increment(ref _currentConcurrentHandlers);
            InterlockedExtensions.Max(ref _maxConcurrentHandlers, current);
        }

        public void Exit()
        {
            Interlocked.Decrement(ref _currentConcurrentHandlers);
        }
    }

    private sealed class MdopOneFirstHandler : INotificationHandler<MdopOneNotification>
    {
        private readonly SingleConcurrencyProbe _probe;

        public MdopOneFirstHandler(SingleConcurrencyProbe probe)
        {
            _probe = probe;
        }

        public async Task Handle(MdopOneNotification notification, CancellationToken cancellationToken)
        {
            _probe.Enter();
            try
            {
                await _probe.Release.Task.WaitAsync(cancellationToken);
            }
            finally
            {
                _probe.Exit();
            }
        }
    }

    private sealed class MdopOneSecondHandler : INotificationHandler<MdopOneNotification>
    {
        private readonly SingleConcurrencyProbe _probe;

        public MdopOneSecondHandler(SingleConcurrencyProbe probe)
        {
            _probe = probe;
        }

        public async Task Handle(MdopOneNotification notification, CancellationToken cancellationToken)
        {
            _probe.Enter();
            try
            {
                await _probe.Release.Task.WaitAsync(cancellationToken);
            }
            finally
            {
                _probe.Exit();
            }
        }
    }

    private sealed class ParallelFaultFirstHandler : INotificationHandler<ParallelFaultNotification>
    {
        private readonly List<string> _calls;

        public ParallelFaultFirstHandler(List<string> calls)
        {
            _calls = calls;
        }

        public async Task Handle(ParallelFaultNotification notification, CancellationToken cancellationToken)
        {
            _calls.Add("first");
            await Task.Delay(20, cancellationToken);
            throw new InvalidOperationException("parallel-first");
        }
    }

    private sealed class ParallelFaultSecondHandler : INotificationHandler<ParallelFaultNotification>
    {
        private readonly List<string> _calls;

        public ParallelFaultSecondHandler(List<string> calls)
        {
            _calls = calls;
        }

        public Task Handle(ParallelFaultNotification notification, CancellationToken cancellationToken)
        {
            _calls.Add("second");
            throw new InvalidOperationException("parallel-second");
        }
    }

    private sealed class CancellableParallelFirstHandler : INotificationHandler<CancellableParallelNotification>
    {
        public Task Handle(CancellableParallelNotification notification, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }
    }

    private sealed class CancellableParallelSecondHandler : INotificationHandler<CancellableParallelNotification>
    {
        public Task Handle(CancellableParallelNotification notification, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }
    }

    private sealed class CancellableSequentialFirstHandler : INotificationHandler<CancellableSequentialNotification>
    {
        private readonly List<string> _calls;

        public CancellableSequentialFirstHandler(List<string> calls)
        {
            _calls = calls;
        }

        public Task Handle(CancellableSequentialNotification notification, CancellationToken cancellationToken)
        {
            _calls.Add("seq-first");
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }
    }

    private sealed class CancellableSequentialSecondHandler : INotificationHandler<CancellableSequentialNotification>
    {
        private readonly List<string> _calls;

        public CancellableSequentialSecondHandler(List<string> calls)
        {
            _calls = calls;
        }

        public Task Handle(CancellableSequentialNotification notification, CancellationToken cancellationToken)
        {
            _calls.Add("seq-second");
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }
    }

    private sealed class MultiPublishProbe
    {
        private int _currentConcurrentHandlers;
        private int _maxConcurrentHandlers;
        private int _startedCount;

        public TaskCompletionSource TwoStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int MaxConcurrentHandlers => _maxConcurrentHandlers;

        public void Enter()
        {
            int current = Interlocked.Increment(ref _currentConcurrentHandlers);
            InterlockedExtensions.Max(ref _maxConcurrentHandlers, current);
            if (Interlocked.Increment(ref _startedCount) == 2)
            {
                TwoStarted.TrySetResult();
            }
        }

        public void Exit()
        {
            Interlocked.Decrement(ref _currentConcurrentHandlers);
        }
    }

    private sealed class PerPublishLimitFirstHandler : INotificationHandler<PerPublishLimitNotification>
    {
        private readonly MultiPublishProbe _probe;

        public PerPublishLimitFirstHandler(MultiPublishProbe probe)
        {
            _probe = probe;
        }

        public async Task Handle(PerPublishLimitNotification notification, CancellationToken cancellationToken)
        {
            _probe.Enter();
            try
            {
                await _probe.Release.Task.WaitAsync(cancellationToken);
            }
            finally
            {
                _probe.Exit();
            }
        }
    }

    private sealed class PerPublishLimitSecondHandler : INotificationHandler<PerPublishLimitNotification>
    {
        private readonly MultiPublishProbe _probe;

        public PerPublishLimitSecondHandler(MultiPublishProbe probe)
        {
            _probe = probe;
        }

        public async Task Handle(PerPublishLimitNotification notification, CancellationToken cancellationToken)
        {
            _probe.Enter();
            try
            {
                await _probe.Release.Task.WaitAsync(cancellationToken);
            }
            finally
            {
                _probe.Exit();
            }
        }
    }

    private sealed class MixedSequentialCancelHandler : INotificationHandler<MixedSequentialFaultNotification>
    {
        private readonly List<string> _calls;

        public MixedSequentialCancelHandler(List<string> calls)
        {
            _calls = calls;
        }

        public Task Handle(MixedSequentialFaultNotification notification, CancellationToken cancellationToken)
        {
            _calls.Add("mixed-cancel");
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }
    }

    private sealed class MixedSequentialFaultHandler : INotificationHandler<MixedSequentialFaultNotification>
    {
        private readonly List<string> _calls;

        public MixedSequentialFaultHandler(List<string> calls)
        {
            _calls = calls;
        }

        public Task Handle(MixedSequentialFaultNotification notification, CancellationToken cancellationToken)
        {
            _calls.Add("mixed-fault");
            throw new InvalidOperationException("mixed-fault");
        }
    }

    private sealed class MdopOneFaultFirstHandler : INotificationHandler<MdopOneFaultNotification>
    {
        public Task Handle(MdopOneFaultNotification notification, CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("mdop1-first");
        }
    }

    private sealed class MdopOneFaultSecondHandler : INotificationHandler<MdopOneFaultNotification>
    {
        public Task Handle(MdopOneFaultNotification notification, CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("mdop1-second");
        }
    }

    private sealed class MixedSequentialFirstHandler : INotificationHandler<MixedSequentialNotification>
    {
        private readonly List<string> _calls;

        public MixedSequentialFirstHandler(List<string> calls)
        {
            _calls = calls;
        }

        public Task Handle(MixedSequentialNotification notification, CancellationToken cancellationToken)
        {
            _calls.Add("seq-1");
            return Task.CompletedTask;
        }
    }

    private sealed class MixedSequentialSecondHandler : INotificationHandler<MixedSequentialNotification>
    {
        private readonly List<string> _calls;

        public MixedSequentialSecondHandler(List<string> calls)
        {
            _calls = calls;
        }

        public Task Handle(MixedSequentialNotification notification, CancellationToken cancellationToken)
        {
            _calls.Add("seq-2");
            return Task.CompletedTask;
        }
    }

    private sealed class PublishStressCounter
    {
        public int FailFastFirstCalls;
        public int FailFastSecondCalls;
        public int AggregateSuccessCalls;
        public int AggregateFailFirstCalls;
        public int AggregateFailSecondCalls;
        public int ParallelCalls;
        public int ParallelCurrentConcurrentCalls;
        public int ParallelMaxConcurrentCalls;
    }

    private sealed class StressFailFastFirstHandler : INotificationHandler<StressFailFastNotification>
    {
        private readonly PublishStressCounter _counter;

        public StressFailFastFirstHandler(PublishStressCounter counter)
        {
            _counter = counter;
        }

        public Task Handle(StressFailFastNotification notification, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _counter.FailFastFirstCalls);
            throw new InvalidOperationException("stress-ff");
        }
    }

    private sealed class StressFailFastSecondHandler : INotificationHandler<StressFailFastNotification>
    {
        private readonly PublishStressCounter _counter;

        public StressFailFastSecondHandler(PublishStressCounter counter)
        {
            _counter = counter;
        }

        public Task Handle(StressFailFastNotification notification, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _counter.FailFastSecondCalls);
            return Task.CompletedTask;
        }
    }

    private sealed class StressAggregateSuccessHandler : INotificationHandler<StressAggregateNotification>
    {
        private readonly PublishStressCounter _counter;

        public StressAggregateSuccessHandler(PublishStressCounter counter)
        {
            _counter = counter;
        }

        public Task Handle(StressAggregateNotification notification, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _counter.AggregateSuccessCalls);
            return Task.CompletedTask;
        }
    }

    private sealed class StressAggregateFirstFailHandler : INotificationHandler<StressAggregateNotification>
    {
        private readonly PublishStressCounter _counter;

        public StressAggregateFirstFailHandler(PublishStressCounter counter)
        {
            _counter = counter;
        }

        public Task Handle(StressAggregateNotification notification, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _counter.AggregateFailFirstCalls);
            throw new InvalidOperationException("stress-agg-1");
        }
    }

    private sealed class StressAggregateSecondFailHandler : INotificationHandler<StressAggregateNotification>
    {
        private readonly PublishStressCounter _counter;

        public StressAggregateSecondFailHandler(PublishStressCounter counter)
        {
            _counter = counter;
        }

        public Task Handle(StressAggregateNotification notification, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _counter.AggregateFailSecondCalls);
            throw new InvalidOperationException("stress-agg-2");
        }
    }

    private sealed class StressParallelFirstHandler : INotificationHandler<StressParallelNotification>
    {
        private readonly PublishStressCounter _counter;

        public StressParallelFirstHandler(PublishStressCounter counter)
        {
            _counter = counter;
        }

        public async Task Handle(StressParallelNotification notification, CancellationToken cancellationToken)
        {
            EnterParallel();
            try
            {
                await Task.Delay(2, cancellationToken);
            }
            finally
            {
                ExitParallel();
            }
        }

        private void EnterParallel()
        {
            Interlocked.Increment(ref _counter.ParallelCalls);
            int current = Interlocked.Increment(ref _counter.ParallelCurrentConcurrentCalls);
            InterlockedExtensions.Max(ref _counter.ParallelMaxConcurrentCalls, current);
        }

        private void ExitParallel()
        {
            Interlocked.Decrement(ref _counter.ParallelCurrentConcurrentCalls);
        }
    }

    private sealed class StressParallelSecondHandler : INotificationHandler<StressParallelNotification>
    {
        private readonly PublishStressCounter _counter;

        public StressParallelSecondHandler(PublishStressCounter counter)
        {
            _counter = counter;
        }

        public async Task Handle(StressParallelNotification notification, CancellationToken cancellationToken)
        {
            EnterParallel();
            try
            {
                await Task.Delay(2, cancellationToken);
            }
            finally
            {
                ExitParallel();
            }
        }

        private void EnterParallel()
        {
            Interlocked.Increment(ref _counter.ParallelCalls);
            int current = Interlocked.Increment(ref _counter.ParallelCurrentConcurrentCalls);
            InterlockedExtensions.Max(ref _counter.ParallelMaxConcurrentCalls, current);
        }

        private void ExitParallel()
        {
            Interlocked.Decrement(ref _counter.ParallelCurrentConcurrentCalls);
        }
    }

    private sealed class StressParallelThirdHandler : INotificationHandler<StressParallelNotification>
    {
        private readonly PublishStressCounter _counter;

        public StressParallelThirdHandler(PublishStressCounter counter)
        {
            _counter = counter;
        }

        public async Task Handle(StressParallelNotification notification, CancellationToken cancellationToken)
        {
            EnterParallel();
            try
            {
                await Task.Delay(2, cancellationToken);
            }
            finally
            {
                ExitParallel();
            }
        }

        private void EnterParallel()
        {
            Interlocked.Increment(ref _counter.ParallelCalls);
            int current = Interlocked.Increment(ref _counter.ParallelCurrentConcurrentCalls);
            InterlockedExtensions.Max(ref _counter.ParallelMaxConcurrentCalls, current);
        }

        private void ExitParallel()
        {
            Interlocked.Decrement(ref _counter.ParallelCurrentConcurrentCalls);
        }
    }

    private sealed class MixedOverrideCounter
    {
        public int FailFastFirstCalls;
        public int FailFastSecondCalls;
        public int AggregateSuccessCalls;
        public int AggregateFailFirstCalls;
        public int AggregateFailSecondCalls;
        public int ParallelCalls;
        public int ParallelCurrentConcurrentCalls;
        public int ParallelMaxConcurrentCalls;
    }

    private sealed class MixedOverrideFailFastFirstHandler : INotificationHandler<MixedOverrideFailFastNotification>
    {
        private readonly MixedOverrideCounter _counter;

        public MixedOverrideFailFastFirstHandler(MixedOverrideCounter counter)
        {
            _counter = counter;
        }

        public Task Handle(MixedOverrideFailFastNotification notification, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _counter.FailFastFirstCalls);
            throw new InvalidOperationException("mixed-ff");
        }
    }

    private sealed class MixedOverrideFailFastSecondHandler : INotificationHandler<MixedOverrideFailFastNotification>
    {
        private readonly MixedOverrideCounter _counter;

        public MixedOverrideFailFastSecondHandler(MixedOverrideCounter counter)
        {
            _counter = counter;
        }

        public Task Handle(MixedOverrideFailFastNotification notification, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _counter.FailFastSecondCalls);
            return Task.CompletedTask;
        }
    }

    private sealed class MixedOverrideAggregateSuccessHandler : INotificationHandler<MixedOverrideAggregateNotification>
    {
        private readonly MixedOverrideCounter _counter;

        public MixedOverrideAggregateSuccessHandler(MixedOverrideCounter counter)
        {
            _counter = counter;
        }

        public Task Handle(MixedOverrideAggregateNotification notification, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _counter.AggregateSuccessCalls);
            return Task.CompletedTask;
        }
    }

    private sealed class MixedOverrideAggregateFailFirstHandler : INotificationHandler<MixedOverrideAggregateNotification>
    {
        private readonly MixedOverrideCounter _counter;

        public MixedOverrideAggregateFailFirstHandler(MixedOverrideCounter counter)
        {
            _counter = counter;
        }

        public Task Handle(MixedOverrideAggregateNotification notification, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _counter.AggregateFailFirstCalls);
            throw new InvalidOperationException("mixed-agg-1");
        }
    }

    private sealed class MixedOverrideAggregateFailSecondHandler : INotificationHandler<MixedOverrideAggregateNotification>
    {
        private readonly MixedOverrideCounter _counter;

        public MixedOverrideAggregateFailSecondHandler(MixedOverrideCounter counter)
        {
            _counter = counter;
        }

        public Task Handle(MixedOverrideAggregateNotification notification, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _counter.AggregateFailSecondCalls);
            throw new InvalidOperationException("mixed-agg-2");
        }
    }

    private sealed class MixedOverrideParallelFirstHandler : INotificationHandler<MixedOverrideParallelNotification>
    {
        private readonly MixedOverrideCounter _counter;

        public MixedOverrideParallelFirstHandler(MixedOverrideCounter counter)
        {
            _counter = counter;
        }

        public async Task Handle(MixedOverrideParallelNotification notification, CancellationToken cancellationToken)
        {
            EnterParallel();
            try
            {
                await Task.Delay(3, cancellationToken);
            }
            finally
            {
                ExitParallel();
            }
        }

        private void EnterParallel()
        {
            Interlocked.Increment(ref _counter.ParallelCalls);
            int current = Interlocked.Increment(ref _counter.ParallelCurrentConcurrentCalls);
            InterlockedExtensions.Max(ref _counter.ParallelMaxConcurrentCalls, current);
        }

        private void ExitParallel()
        {
            Interlocked.Decrement(ref _counter.ParallelCurrentConcurrentCalls);
        }
    }

    private sealed class MixedOverrideParallelSecondHandler : INotificationHandler<MixedOverrideParallelNotification>
    {
        private readonly MixedOverrideCounter _counter;

        public MixedOverrideParallelSecondHandler(MixedOverrideCounter counter)
        {
            _counter = counter;
        }

        public async Task Handle(MixedOverrideParallelNotification notification, CancellationToken cancellationToken)
        {
            EnterParallel();
            try
            {
                await Task.Delay(3, cancellationToken);
            }
            finally
            {
                ExitParallel();
            }
        }

        private void EnterParallel()
        {
            Interlocked.Increment(ref _counter.ParallelCalls);
            int current = Interlocked.Increment(ref _counter.ParallelCurrentConcurrentCalls);
            InterlockedExtensions.Max(ref _counter.ParallelMaxConcurrentCalls, current);
        }

        private void ExitParallel()
        {
            Interlocked.Decrement(ref _counter.ParallelCurrentConcurrentCalls);
        }
    }

    private sealed class ScopedParallelProbe
    {
        private int _globalCurrentConcurrentCalls;
        private int _globalMaxConcurrentCalls;

        public ConcurrentDictionary<int, int> CurrentByPublishId { get; } = [];

        public ConcurrentDictionary<int, int> MaxByPublishId { get; } = [];

        public int GlobalMaxConcurrentCalls => _globalMaxConcurrentCalls;

        public void Enter(int publishId)
        {
            int currentForPublish = CurrentByPublishId.AddOrUpdate(publishId, 1, static (_, current) => current + 1);
            MaxByPublishId.AddOrUpdate(publishId, currentForPublish, (_, currentMax) => currentForPublish > currentMax ? currentForPublish : currentMax);

            int globalCurrent = Interlocked.Increment(ref _globalCurrentConcurrentCalls);
            InterlockedExtensions.Max(ref _globalMaxConcurrentCalls, globalCurrent);
        }

        public void Exit(int publishId)
        {
            CurrentByPublishId.AddOrUpdate(publishId, 0, static (_, current) => current - 1);
            Interlocked.Decrement(ref _globalCurrentConcurrentCalls);
        }
    }

    private sealed class ScopedParallelFirstHandler : INotificationHandler<ScopedParallelNotification>
    {
        private readonly ScopedParallelProbe _probe;

        public ScopedParallelFirstHandler(ScopedParallelProbe probe)
        {
            _probe = probe;
        }

        public async Task Handle(ScopedParallelNotification notification, CancellationToken cancellationToken)
        {
            _probe.Enter(notification.Id);
            try
            {
                await Task.Delay(20, cancellationToken);
            }
            finally
            {
                _probe.Exit(notification.Id);
            }
        }
    }

    private sealed class ScopedParallelSecondHandler : INotificationHandler<ScopedParallelNotification>
    {
        private readonly ScopedParallelProbe _probe;

        public ScopedParallelSecondHandler(ScopedParallelProbe probe)
        {
            _probe = probe;
        }

        public async Task Handle(ScopedParallelNotification notification, CancellationToken cancellationToken)
        {
            _probe.Enter(notification.Id);
            try
            {
                await Task.Delay(20, cancellationToken);
            }
            finally
            {
                _probe.Exit(notification.Id);
            }
        }
    }

    private sealed class RepeatParallelFirstHandler : INotificationHandler<RepeatParallelNotification>
    {
        public async Task Handle(RepeatParallelNotification notification, CancellationToken cancellationToken)
        {
            await Task.Delay(2, cancellationToken);
        }
    }

    private sealed class RepeatParallelSecondHandler : INotificationHandler<RepeatParallelNotification>
    {
        public async Task Handle(RepeatParallelNotification notification, CancellationToken cancellationToken)
        {
            await Task.Delay(2, cancellationToken);
        }
    }

    private static class InterlockedExtensions
    {
        public static void Max(ref int location, int value)
        {
            while (true)
            {
                int current = Volatile.Read(ref location);
                if (value <= current)
                {
                    return;
                }

                if (Interlocked.CompareExchange(ref location, value, current) == current)
                {
                    return;
                }
            }
        }
    }
}
