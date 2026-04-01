using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;

namespace Mediora.Tests;

public sealed class ServiceCollectionExtensionsTests
{
    [Fact]
    public void AddMediora_ThrowsArgumentNullException_WhenServicesIsNull()
    {
        var exception = Assert.Throws<ArgumentNullException>(
            () => ServiceCollectionExtensions.AddMediora(null!, static _ => { }));

        Assert.Equal("services", exception.ParamName);
    }

    [Fact]
    public void AddMediora_ThrowsArgumentNullException_WhenConfigureIsNull()
    {
        var services = new ServiceCollection();

        var exception = Assert.Throws<ArgumentNullException>(
            () => services.AddMediora(null!));

        Assert.Equal("configure", exception.ParamName);
    }

    [Fact]
    public void AddMediora_ThrowsInvalidOperationException_WhenNoAssembliesAreConfigured()
    {
        var services = new ServiceCollection();

        var exception = Assert.Throws<InvalidOperationException>(() => services.AddMediora(static _ => { }));

        Assert.Contains("No assemblies were configured", exception.Message);
    }

    [Fact]
    public async Task AddMediora_RegistersHandlersFromAssembly()
    {
        var services = new ServiceCollection();
        services.AddMediora(options => options.RegisterServicesFromAssembly(typeof(AssemblyRequestHandler).Assembly));

        await using var provider = services.BuildServiceProvider();
        var sender = provider.GetRequiredService<ISender>();

        var response = await sender.Send(new AssemblyRequest());

        Assert.Equal("assembly-handler", response);
    }

    [Fact]
    public void AddMediora_RegistersMediatorAbstractions()
    {
        var services = new ServiceCollection();
        services.AddMediora(options => options.RegisterServicesFromAssembly(typeof(AssemblyRequestHandler).Assembly));

        using var provider = services.BuildServiceProvider();

        _ = provider.GetRequiredService<IMediator>();
        _ = provider.GetRequiredService<ISender>();
        _ = provider.GetRequiredService<IPublisher>();
    }

    [Fact]
    public async Task AddMediora_EndToEndSend_ReturnsExpectedResponse()
    {
        var services = new ServiceCollection();
        services.AddMediora(options => options.RegisterServicesFromAssembly(typeof(EndToEndHandler).Assembly));

        await using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        var response = await mediator.Send(new EndToEndRequest("mediora"));

        Assert.Equal("MEDIORA", response);
    }

    [Fact]
    public async Task AddMediora_RegistersStreamHandlersFromAssembly()
    {
        var services = new ServiceCollection();
        services.AddMediora(options => options.RegisterServicesFromAssembly(typeof(AssemblyStreamRequestHandler).Assembly));

        await using var provider = services.BuildServiceProvider();
        var sender = provider.GetRequiredService<ISender>();

        List<string> values = [];

        await foreach (var value in sender.CreateStream(new AssemblyStreamRequest("mediora")))
        {
            values.Add(value);
        }

        Assert.Equal(["mediora", "MEDIORA"], values);
    }

    [Fact]
    public void AddMediora_TransientLifetime_ResolvesDifferentInstances()
    {
        var services = new ServiceCollection();
        services.AddMediora(options =>
        {
            options.Lifetime = ServiceLifetime.Transient;
            options.RegisterServicesFromAssembly(typeof(AssemblyRequestHandler).Assembly);
        });

        using var provider = services.BuildServiceProvider();

        var first = provider.GetRequiredService<IMediator>();
        var second = provider.GetRequiredService<IMediator>();

        Assert.NotSame(first, second);
    }

    [Fact]
    public void AddMediora_ScopedLifetime_ResolvesSameWithinScopeAndDifferentAcrossScopes()
    {
        var services = new ServiceCollection();
        services.AddMediora(options =>
        {
            options.Lifetime = ServiceLifetime.Scoped;
            options.RegisterServicesFromAssembly(typeof(AssemblyRequestHandler).Assembly);
        });

        using var provider = services.BuildServiceProvider();
        using var scope1 = provider.CreateScope();
        using var scope2 = provider.CreateScope();

        var scope1First = scope1.ServiceProvider.GetRequiredService<IMediator>();
        var scope1Second = scope1.ServiceProvider.GetRequiredService<IMediator>();
        var scope2Mediator = scope2.ServiceProvider.GetRequiredService<IMediator>();

        Assert.Same(scope1First, scope1Second);
        Assert.NotSame(scope1First, scope2Mediator);
    }

    [Fact]
    public void AddMediora_SingletonLifetime_ResolvesSameAcrossScopes()
    {
        var services = new ServiceCollection();
        services.AddMediora(options =>
        {
            options.Lifetime = ServiceLifetime.Singleton;
            options.RegisterServicesFromAssembly(typeof(AssemblyRequestHandler).Assembly);
        });

        using var provider = services.BuildServiceProvider();
        using var scope1 = provider.CreateScope();
        using var scope2 = provider.CreateScope();

        var rootMediator = provider.GetRequiredService<IMediator>();
        var scope1Mediator = scope1.ServiceProvider.GetRequiredService<IMediator>();
        var scope2Mediator = scope2.ServiceProvider.GetRequiredService<IMediator>();

        Assert.Same(rootMediator, scope1Mediator);
        Assert.Same(rootMediator, scope2Mediator);
    }

    [Fact]
    public void AddMediora_CalledTwice_DoesNotDuplicateRegistrations()
    {
        var services = new ServiceCollection();

        services.AddMediora(options =>
        {
            options.RegisterServicesFromAssembly(typeof(IdempotentRequestHandler).Assembly);
            options.AddBehavior<IdempotentBehavior>();
        });

        services.AddMediora(options =>
        {
            options.RegisterServicesFromAssembly(typeof(IdempotentRequestHandler).Assembly);
            options.AddBehavior<IdempotentBehavior>();
        });

        using var provider = services.BuildServiceProvider();

        var requestHandler = provider.GetServices<IRequestHandler<IdempotentRequest, string>>();
        var notificationHandlers = provider.GetServices<INotificationHandler<IdempotentNotification>>();
        var pipelineBehaviors = provider.GetServices<IPipelineBehavior<IdempotentRequest, string>>();

        Assert.Single(requestHandler);
        Assert.Single(notificationHandlers);
        Assert.Equal(pipelineBehaviors.Select(static behavior => behavior.GetType()).Distinct().Count(), pipelineBehaviors.Count());
        Assert.Single(pipelineBehaviors.OfType<IdempotentBehavior>());
    }

    [Fact]
    public void AddMediora_DoesNotScanPipelineBehaviorsFromAssemblies()
    {
        var services = new ServiceCollection();

        services.AddMediora(options => options.RegisterServicesFromAssembly(typeof(IdempotentRequestHandler).Assembly));

        using var provider = services.BuildServiceProvider();
        var pipelineBehaviors = provider.GetServices<IPipelineBehavior<IdempotentRequest, string>>();

        Assert.Empty(pipelineBehaviors);
    }

    [Fact]
    public void AddMediora_DoesNotScanStreamPipelineBehaviorsFromAssemblies()
    {
        var services = new ServiceCollection();

        services.AddMediora(options => options.RegisterServicesFromAssembly(typeof(IdempotentRequestHandler).Assembly));

        using var provider = services.BuildServiceProvider();
        var streamBehaviors = provider.GetServices<IStreamPipelineBehavior<AssemblyStreamRequest, string>>();

        Assert.Empty(streamBehaviors);
    }

    [Fact]
    public void AddMediora_ThrowsArgumentNullException_WhenAddOpenBehaviorReceivesNull()
    {
        var services = new ServiceCollection();

        ArgumentNullException exception = Assert.Throws<ArgumentNullException>(() => services.AddMediora(options =>
        {
            options.RegisterServicesFromAssembly(typeof(AssemblyRequestHandler).Assembly);
            options.AddOpenBehavior(null!);
        }));

        Assert.Equal("openBehaviorType", exception.ParamName);
    }

    [Fact]
    public void AddMediora_ThrowsArgumentNullException_WhenAddOpenStreamBehaviorReceivesNull()
    {
        var services = new ServiceCollection();

        ArgumentNullException exception = Assert.Throws<ArgumentNullException>(() => services.AddMediora(options =>
        {
            options.RegisterServicesFromAssembly(typeof(AssemblyRequestHandler).Assembly);
            options.AddOpenStreamBehavior(null!);
        }));

        Assert.Equal("openBehaviorType", exception.ParamName);
    }

    [Fact]
    public void AddMediora_ThrowsArgumentException_WhenOpenBehaviorTypeIsInvalid()
    {
        var services = new ServiceCollection();

        var exception = Assert.Throws<ArgumentException>(() => services.AddMediora(options =>
        {
            options.RegisterServicesFromAssembly(typeof(AssemblyRequestHandler).Assembly);
            options.AddOpenBehavior(typeof(IdempotentBehavior));
        }));

        Assert.Contains("open generic type definition", exception.Message);
    }

    [Fact]
    public void AddMediora_ThrowsArgumentException_WhenAddOpenBehaviorTypeDoesNotImplementContract()
    {
        var services = new ServiceCollection();

        var exception = Assert.Throws<ArgumentException>(() => services.AddMediora(options =>
        {
            options.RegisterServicesFromAssembly(typeof(AssemblyRequestHandler).Assembly);
            options.AddOpenBehavior(typeof(InvalidOpenRequestBehavior<,>));
        }));

        Assert.Contains("must implement IPipelineBehavior", exception.Message);
    }

    [Fact]
    public void AddMediora_ThrowsArgumentException_WhenOpenStreamBehaviorTypeIsInvalid()
    {
        var services = new ServiceCollection();

        var exception = Assert.Throws<ArgumentException>(() => services.AddMediora(options =>
        {
            options.RegisterServicesFromAssembly(typeof(AssemblyRequestHandler).Assembly);
            options.AddOpenStreamBehavior(typeof(IdempotentStreamBehavior));
        }));

        Assert.Contains("open generic type definition", exception.Message);
    }

    [Fact]
    public void AddMediora_ThrowsArgumentException_WhenAddOpenStreamBehaviorTypeDoesNotImplementContract()
    {
        var services = new ServiceCollection();

        var exception = Assert.Throws<ArgumentException>(() => services.AddMediora(options =>
        {
            options.RegisterServicesFromAssembly(typeof(AssemblyRequestHandler).Assembly);
            options.AddOpenStreamBehavior(typeof(InvalidOpenStreamBehavior<,>));
        }));

        Assert.Contains("must implement IStreamPipelineBehavior", exception.Message);
    }

    [Fact]
    public void AddMediora_ThrowsArgumentException_WhenAddBehaviorReceivesOpenGenericType()
    {
        var services = new ServiceCollection();

        var exception = Assert.Throws<ArgumentException>(() => services.AddMediora(options =>
        {
            options.RegisterServicesFromAssembly(typeof(AssemblyRequestHandler).Assembly);
            options.AddBehavior(typeof(DeduplicatedOpenBehavior<,>));
        }));

        Assert.Contains("open generic", exception.Message);
    }

    [Fact]
    public void AddMediora_ThrowsArgumentException_WhenAddStreamBehaviorReceivesOpenGenericType()
    {
        var services = new ServiceCollection();

        var exception = Assert.Throws<ArgumentException>(() => services.AddMediora(options =>
        {
            options.RegisterServicesFromAssembly(typeof(AssemblyRequestHandler).Assembly);
            options.AddStreamBehavior(typeof(DeduplicatedOpenStreamBehavior<,>));
        }));

        Assert.Contains("open generic", exception.Message);
    }

    [Fact]
    public void AddMediora_ThrowsArgumentException_WhenAddBehaviorReceivesNonClassType()
    {
        var services = new ServiceCollection();

        var exception = Assert.Throws<ArgumentException>(() => services.AddMediora(options =>
        {
            options.RegisterServicesFromAssembly(typeof(AssemblyRequestHandler).Assembly);
            options.AddBehavior(typeof(IPipelineBehavior<IdempotentRequest, string>));
        }));

        Assert.Contains("non-abstract class", exception.Message);
    }

    [Fact]
    public void AddMediora_ThrowsArgumentException_WhenAddStreamBehaviorReceivesNonClassType()
    {
        var services = new ServiceCollection();

        var exception = Assert.Throws<ArgumentException>(() => services.AddMediora(options =>
        {
            options.RegisterServicesFromAssembly(typeof(AssemblyRequestHandler).Assembly);
            options.AddStreamBehavior(typeof(IStreamPipelineBehavior<AssemblyStreamRequest, string>));
        }));

        Assert.Contains("non-abstract class", exception.Message);
    }

    [Fact]
    public void AddMediora_ThrowsArgumentException_WhenAddBehaviorReceivesAbstractClass()
    {
        var services = new ServiceCollection();

        var exception = Assert.Throws<ArgumentException>(() => services.AddMediora(options =>
        {
            options.RegisterServicesFromAssembly(typeof(AssemblyRequestHandler).Assembly);
            options.AddBehavior(typeof(AbstractClosedBehavior));
        }));

        Assert.Contains("non-abstract class", exception.Message);
    }

    [Fact]
    public void AddMediora_ThrowsArgumentException_WhenAddStreamBehaviorReceivesAbstractClass()
    {
        var services = new ServiceCollection();

        var exception = Assert.Throws<ArgumentException>(() => services.AddMediora(options =>
        {
            options.RegisterServicesFromAssembly(typeof(AssemblyRequestHandler).Assembly);
            options.AddStreamBehavior(typeof(AbstractClosedStreamBehavior));
        }));

        Assert.Contains("non-abstract class", exception.Message);
    }

    [Fact]
    public void AddMediora_ThrowsArgumentNullException_WhenAddBehaviorReceivesNull()
    {
        var services = new ServiceCollection();

        ArgumentNullException exception = Assert.Throws<ArgumentNullException>(() => services.AddMediora(options =>
        {
            options.RegisterServicesFromAssembly(typeof(AssemblyRequestHandler).Assembly);
            options.AddBehavior((Type)null!);
        }));

        Assert.Equal("behaviorType", exception.ParamName);
    }

    [Fact]
    public void AddMediora_ThrowsArgumentNullException_WhenAddStreamBehaviorReceivesNull()
    {
        var services = new ServiceCollection();

        ArgumentNullException exception = Assert.Throws<ArgumentNullException>(() => services.AddMediora(options =>
        {
            options.RegisterServicesFromAssembly(typeof(AssemblyRequestHandler).Assembly);
            options.AddStreamBehavior((Type)null!);
        }));

        Assert.Equal("behaviorType", exception.ParamName);
    }

    [Fact]
    public void AddMediora_ThrowsArgumentException_WhenClosedStreamBehaviorTypeDoesNotImplementContract()
    {
        var services = new ServiceCollection();

        var exception = Assert.Throws<ArgumentException>(() => services.AddMediora(options =>
        {
            options.RegisterServicesFromAssembly(typeof(AssemblyRequestHandler).Assembly);
            options.AddStreamBehavior(typeof(AssemblyStreamRequestHandler));
        }));

        Assert.Contains("must implement IStreamPipelineBehavior", exception.Message);
    }

    [Fact]
    public void AddMediora_ThrowsArgumentException_WhenAddOpenBehaviorReceivesNonClassType()
    {
        var services = new ServiceCollection();

        var exception = Assert.Throws<ArgumentException>(() => services.AddMediora(options =>
        {
            options.RegisterServicesFromAssembly(typeof(AssemblyRequestHandler).Assembly);
            options.AddOpenBehavior(typeof(IPipelineBehavior<,>));
        }));

        Assert.Contains("non-abstract class", exception.Message);
    }

    [Fact]
    public void AddMediora_ThrowsArgumentException_WhenAddOpenStreamBehaviorReceivesNonClassType()
    {
        var services = new ServiceCollection();

        var exception = Assert.Throws<ArgumentException>(() => services.AddMediora(options =>
        {
            options.RegisterServicesFromAssembly(typeof(AssemblyRequestHandler).Assembly);
            options.AddOpenStreamBehavior(typeof(IStreamPipelineBehavior<,>));
        }));

        Assert.Contains("non-abstract class", exception.Message);
    }

    [Fact]
    public void AddMediora_ThrowsArgumentException_WhenClosedBehaviorTypeDoesNotImplementContract()
    {
        var services = new ServiceCollection();

        var exception = Assert.Throws<ArgumentException>(() => services.AddMediora(options =>
        {
            options.RegisterServicesFromAssembly(typeof(AssemblyRequestHandler).Assembly);
            options.AddBehavior(typeof(AssemblyRequestHandler));
        }));

        Assert.Contains("must implement IPipelineBehavior", exception.Message);
    }

    [Fact]
    public void AddMediora_RegistersClosedBehavior_WithAddBehavior()
    {
        var services = new ServiceCollection();

        services.AddMediora(options =>
        {
            options.RegisterServicesFromAssembly(typeof(IdempotentRequestHandler).Assembly);
            options.AddBehavior<IdempotentBehavior>();
        });

        using var provider = services.BuildServiceProvider();
        var behaviors = provider.GetServices<IPipelineBehavior<IdempotentRequest, string>>();

        Assert.Single(behaviors);
        Assert.IsType<IdempotentBehavior>(behaviors.Single());
    }

    [Fact]
    public void AddMediora_RegistersClosedStreamBehavior_WithAddStreamBehavior()
    {
        var services = new ServiceCollection();

        services.AddMediora(options =>
        {
            options.RegisterServicesFromAssembly(typeof(AssemblyStreamRequestHandler).Assembly);
            options.AddStreamBehavior<IdempotentStreamBehavior>();
        });

        using var provider = services.BuildServiceProvider();
        var behaviors = provider.GetServices<IStreamPipelineBehavior<AssemblyStreamRequest, string>>();

        Assert.Single(behaviors);
        Assert.IsType<IdempotentStreamBehavior>(behaviors.Single());
    }

    [Fact]
    public void AddMediora_DoesNotDuplicateOpenBehavior_WhenConfiguredTwice()
    {
        var services = new ServiceCollection();

        services.AddMediora(options =>
        {
            options.RegisterServicesFromAssembly(typeof(IdempotentRequestHandler).Assembly);
            options.AddOpenBehavior(typeof(DeduplicatedOpenBehavior<,>));
            options.AddOpenBehavior(typeof(DeduplicatedOpenBehavior<,>));
        });

        using var provider = services.BuildServiceProvider();
        var behaviors = provider.GetServices<IPipelineBehavior<IdempotentRequest, string>>();

        Assert.Single(behaviors);
        Assert.IsType<DeduplicatedOpenBehavior<IdempotentRequest, string>>(behaviors.Single());
    }

    [Fact]
    public void AddMediora_DoesNotDuplicateOpenStreamBehavior_WhenConfiguredTwice()
    {
        var services = new ServiceCollection();

        services.AddMediora(options =>
        {
            options.RegisterServicesFromAssembly(typeof(AssemblyStreamRequestHandler).Assembly);
            options.AddOpenStreamBehavior(typeof(DeduplicatedOpenStreamBehavior<,>));
            options.AddOpenStreamBehavior(typeof(DeduplicatedOpenStreamBehavior<,>));
        });

        using var provider = services.BuildServiceProvider();
        var behaviors = provider.GetServices<IStreamPipelineBehavior<AssemblyStreamRequest, string>>();

        Assert.Single(behaviors);
        Assert.IsType<DeduplicatedOpenStreamBehavior<AssemblyStreamRequest, string>>(behaviors.Single());
    }

    [Fact]
    public void AddMediora_DoesNotDuplicateClosedBehavior_WhenConfiguredTwice()
    {
        var services = new ServiceCollection();

        services.AddMediora(options =>
        {
            options.RegisterServicesFromAssembly(typeof(IdempotentRequestHandler).Assembly);
            options.AddBehavior<IdempotentBehavior>();
            options.AddBehavior<IdempotentBehavior>();
        });

        using var provider = services.BuildServiceProvider();
        var behaviors = provider.GetServices<IPipelineBehavior<IdempotentRequest, string>>();

        Assert.Single(behaviors);
        Assert.IsType<IdempotentBehavior>(behaviors.Single());
    }

    [Fact]
    public void AddMediora_DoesNotDuplicateClosedStreamBehavior_WhenConfiguredTwice()
    {
        var services = new ServiceCollection();

        services.AddMediora(options =>
        {
            options.RegisterServicesFromAssembly(typeof(AssemblyStreamRequestHandler).Assembly);
            options.AddStreamBehavior<IdempotentStreamBehavior>();
            options.AddStreamBehavior<IdempotentStreamBehavior>();
        });

        using var provider = services.BuildServiceProvider();
        var behaviors = provider.GetServices<IStreamPipelineBehavior<AssemblyStreamRequest, string>>();

        Assert.Single(behaviors);
        Assert.IsType<IdempotentStreamBehavior>(behaviors.Single());
    }

    [Fact]
    public void AddMediora_RegisterServicesFromAssemblies_DeduplicatesRepeatedAssemblies()
    {
        var services = new ServiceCollection();

        services.AddMediora(options =>
        {
            Assembly assembly = typeof(AssemblyRequestHandler).Assembly;
            options.RegisterServicesFromAssemblies([assembly, assembly]);
        });

        using var provider = services.BuildServiceProvider();
        var handlers = provider.GetServices<IRequestHandler<AssemblyRequest, string>>();

        Assert.Single(handlers);
        Assert.IsType<AssemblyRequestHandler>(handlers.Single());
    }

    [Fact]
    public void AddMediora_ThrowsInvalidOperationException_WhenDuplicateRequestHandlersExist()
    {
        var method = typeof(ServiceCollectionExtensions).GetMethod(
            "ValidateRequestHandlerUniqueness",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);

        var registrationType = typeof(MedioraServiceConfiguration).GetNestedType(
            "ServiceRegistration",
            BindingFlags.NonPublic);

        Assert.NotNull(registrationType);

        var registrationListType = typeof(List<>).MakeGenericType(registrationType!);
        var registrations = Activator.CreateInstance(registrationListType)!;

        var addMethod = registrationListType.GetMethod("Add")!;
        addMethod.Invoke(registrations, [
            Activator.CreateInstance(
                registrationType!,
                typeof(IRequestHandler<AssemblyRequest, string>),
                typeof(AssemblyRequestHandler),
                typeof(IRequestHandler<,>))!]);
        addMethod.Invoke(registrations, [
            Activator.CreateInstance(
                registrationType!,
                typeof(IRequestHandler<AssemblyRequest, string>),
                typeof(EndToEndHandler),
                typeof(IRequestHandler<,>))!]);

        var exception = Assert.Throws<TargetInvocationException>(() => method!.Invoke(null, [new ServiceCollection(), registrations]));

        Assert.IsType<InvalidOperationException>(exception.InnerException);
        Assert.Contains("Multiple IRequestHandler implementations", exception.InnerException!.Message);
    }

    [Fact]
    public void AddMediora_ThrowsInvalidOperationException_WhenDuplicateStreamRequestHandlersExist()
    {
        var method = typeof(ServiceCollectionExtensions).GetMethod(
            "ValidateRequestHandlerUniqueness",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);

        var registrationType = typeof(MedioraServiceConfiguration).GetNestedType(
            "ServiceRegistration",
            BindingFlags.NonPublic);

        Assert.NotNull(registrationType);

        var registrationListType = typeof(List<>).MakeGenericType(registrationType!);
        var registrations = Activator.CreateInstance(registrationListType)!;

        var addMethod = registrationListType.GetMethod("Add")!;
        addMethod.Invoke(registrations, [
            Activator.CreateInstance(
                registrationType!,
                typeof(IStreamRequestHandler<DuplicateStreamRequest, string>),
                typeof(AssemblyRequestHandler),
                typeof(IStreamRequestHandler<,>))!]);
        addMethod.Invoke(registrations, [
            Activator.CreateInstance(
                registrationType!,
                typeof(IStreamRequestHandler<DuplicateStreamRequest, string>),
                typeof(EndToEndHandler),
                typeof(IStreamRequestHandler<,>))!]);

        var exception = Assert.Throws<TargetInvocationException>(() => method!.Invoke(null, [new ServiceCollection(), registrations]));

        Assert.IsType<InvalidOperationException>(exception.InnerException);
        Assert.Contains("Multiple IStreamRequestHandler implementations", exception.InnerException!.Message);
    }

    [Fact]
    public void GetLoadableTypes_WhenAssemblyThrowsReflectionTypeLoadException_ReturnsOnlyNonNullTypes()
    {
        var method = typeof(MedioraServiceConfiguration).GetMethod(
            "GetLoadableTypes",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);

        var assembly = new ReflectionTypeLoadExceptionAssembly();
        var types = Assert.IsType<Type[]>(method!.Invoke(null, [assembly]));

        Assert.Single(types);
        Assert.Equal(typeof(AssemblyRequestHandler), types[0]);
    }

    [Fact]
    public void ClearScanCache_RemovesCachedAssemblyRegistrations_ForConfigurationInstance()
    {
        var configuration = new MedioraServiceConfiguration();
        configuration.RegisterServicesFromAssembly(typeof(AssemblyRequestHandler).Assembly);
        _ = configuration.GetServiceRegistrations();

        FieldInfo? field = typeof(MedioraServiceConfiguration).GetField("_scanCache", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(field);

        object? cache = field!.GetValue(configuration);
        Assert.NotNull(cache);

        PropertyInfo? countProperty = cache!.GetType().GetProperty("Count", BindingFlags.Public | BindingFlags.Instance);
        Assert.NotNull(countProperty);

        int countBeforeClear = Assert.IsType<int>(countProperty!.GetValue(cache));
        Assert.True(countBeforeClear > 0);

        configuration.ClearScanCache();

        int countAfterClear = Assert.IsType<int>(countProperty.GetValue(cache));
        Assert.Equal(0, countAfterClear);
    }

    [Fact]
    public void AddMediora_ThrowsInvalidOperationException_WhenSlidingExpirationIsNonPositive()
    {
        var services = new ServiceCollection();

        var exception = Assert.Throws<InvalidOperationException>(() => services.AddMediora(options =>
        {
            options.WrapperCacheSlidingExpiration = TimeSpan.Zero;
            options.RegisterServicesFromAssembly(typeof(AssemblyRequestHandler).Assembly);
        }));

        Assert.Contains("WrapperCacheSlidingExpiration", exception.Message);
    }

    [Fact]
    public void AddMediora_ThrowsInvalidOperationException_WhenMaxRequestWrappersIsNonPositive()
    {
        var services = new ServiceCollection();

        var exception = Assert.Throws<InvalidOperationException>(() => services.AddMediora(options =>
        {
            options.MaxRequestWrappers = 0;
            options.RegisterServicesFromAssembly(typeof(AssemblyRequestHandler).Assembly);
        }));

        Assert.Contains("MaxRequestWrappers", exception.Message);
    }

    [Fact]
    public void AddMediora_ThrowsInvalidOperationException_WhenMaxStreamRequestWrappersIsNonPositive()
    {
        var services = new ServiceCollection();

        var exception = Assert.Throws<InvalidOperationException>(() => services.AddMediora(options =>
        {
            options.MaxStreamRequestWrappers = 0;
            options.RegisterServicesFromAssembly(typeof(AssemblyRequestHandler).Assembly);
        }));

        Assert.Contains("MaxStreamRequestWrappers", exception.Message);
    }

    [Fact]
    public void AddMediora_ThrowsInvalidOperationException_WhenMaxNotificationWrappersIsNonPositive()
    {
        var services = new ServiceCollection();

        var exception = Assert.Throws<InvalidOperationException>(() => services.AddMediora(options =>
        {
            options.MaxNotificationWrappers = 0;
            options.RegisterServicesFromAssembly(typeof(AssemblyRequestHandler).Assembly);
        }));

        Assert.Contains("MaxNotificationWrappers", exception.Message);
    }

    [Fact]
    public void AddMediora_ThrowsInvalidOperationException_WhenMaxWrapperFactoriesIsNonPositive()
    {
        var services = new ServiceCollection();

        var exception = Assert.Throws<InvalidOperationException>(() => services.AddMediora(options =>
        {
            options.MaxWrapperFactories = 0;
            options.RegisterServicesFromAssembly(typeof(AssemblyRequestHandler).Assembly);
        }));

        Assert.Contains("MaxWrapperFactories", exception.Message);
    }

    [Fact]
    public void AddMediora_ThrowsInvalidOperationException_WhenAbsoluteExpirationIsNonPositive()
    {
        var services = new ServiceCollection();

        var exception = Assert.Throws<InvalidOperationException>(() => services.AddMediora(options =>
        {
            options.WrapperCacheAbsoluteExpiration = TimeSpan.Zero;
            options.RegisterServicesFromAssembly(typeof(AssemblyRequestHandler).Assembly);
        }));

        Assert.Contains("WrapperCacheAbsoluteExpiration", exception.Message);
    }

    [Fact]
    public void AddMediora_ThrowsInvalidOperationException_WhenNotificationParallelMaxDegreeOfParallelismIsNonPositive()
    {
        var services = new ServiceCollection();

        var exception = Assert.Throws<InvalidOperationException>(() => services.AddMediora(options =>
        {
            options.NotificationParallelMaxDegreeOfParallelism = 0;
            options.RegisterServicesFromAssembly(typeof(AssemblyRequestHandler).Assembly);
        }));

        Assert.Contains("NotificationParallelMaxDegreeOfParallelism", exception.Message);
    }

    [Fact]
    public void ConfigureNotificationPublishMode_ThrowsArgumentException_WhenTypeDoesNotImplementINotification()
    {
        var configuration = new MedioraServiceConfiguration();

        var exception = Assert.Throws<ArgumentException>(() => configuration.ConfigureNotificationPublishMode(
            NotificationPublishMode.ParallelAggregateAll,
            typeof(string)));

        Assert.Contains("must implement INotification", exception.Message);
    }

    [Fact]
    public void ConfigureNotificationPublishMode_ThrowsArgumentException_WhenTypeArrayContainsNull()
    {
        var configuration = new MedioraServiceConfiguration();

        var exception = Assert.Throws<ArgumentException>(() => configuration.ConfigureNotificationPublishMode(
            NotificationPublishMode.ParallelAggregateAll,
            typeof(IdempotentNotification),
            null!));

        Assert.Contains("cannot contain null entries", exception.Message);
    }

    [Fact]
    public void GetNotificationPublishOptions_UsesDefaultModeAndPerTypeOverrides()
    {
        var configuration = new MedioraServiceConfiguration
        {
            DefaultNotificationPublishMode = NotificationPublishMode.ParallelAggregateAll,
            NotificationParallelMaxDegreeOfParallelism = 4
        };

        configuration.ConfigureNotificationPublishMode<IdempotentNotification>(NotificationPublishMode.SequentialAggregateAll);

        NotificationPublishOptions options = configuration.GetNotificationPublishOptions();

        Assert.Equal(NotificationPublishMode.ParallelAggregateAll, options.DefaultMode);
        Assert.Equal(4, options.ParallelMaxDegreeOfParallelism);
        Assert.Equal(NotificationPublishMode.SequentialAggregateAll, options.Resolve(typeof(IdempotentNotification)));
        Assert.Equal(NotificationPublishMode.ParallelAggregateAll, options.Resolve(typeof(UnconfiguredNotification)));
    }

    [Fact]
    public void ConfigureNotificationPublishMode_LastWriteWins_ForSameNotificationType()
    {
        var configuration = new MedioraServiceConfiguration();

        configuration.ConfigureNotificationPublishMode<IdempotentNotification>(NotificationPublishMode.SequentialAggregateAll);
        configuration.ConfigureNotificationPublishMode(NotificationPublishMode.ParallelAggregateAll, typeof(IdempotentNotification));

        NotificationPublishOptions options = configuration.GetNotificationPublishOptions();

        Assert.Equal(NotificationPublishMode.ParallelAggregateAll, options.Resolve(typeof(IdempotentNotification)));
    }

    [Fact]
    public void AddMediora_ThrowsInvalidOperationException_WhenManualRequestHandlerConflictsWithScannedHandler()
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(IRequestHandler<AssemblyRequest, string>), typeof(ManualConflictRequestHandler));

        var exception = Assert.Throws<InvalidOperationException>(() => services.AddMediora(options =>
            options.RegisterServicesFromAssembly(typeof(AssemblyRequestHandler).Assembly)));

        Assert.Contains("Multiple IRequestHandler implementations", exception.Message);
        string requestServiceName = typeof(IRequestHandler<AssemblyRequest, string>).FullName ?? typeof(IRequestHandler<AssemblyRequest, string>).Name;
        Assert.Contains(requestServiceName, exception.Message);
    }

    [Fact]
    public void AddMediora_ThrowsInvalidOperationException_WhenManualStreamHandlerConflictsWithScannedHandler()
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(IStreamRequestHandler<AssemblyStreamRequest, string>), typeof(ManualConflictStreamHandler));

        var exception = Assert.Throws<InvalidOperationException>(() => services.AddMediora(options =>
            options.RegisterServicesFromAssembly(typeof(AssemblyStreamRequestHandler).Assembly)));

        Assert.Contains("Multiple IStreamRequestHandler implementations", exception.Message);
        string streamServiceName = typeof(IStreamRequestHandler<AssemblyStreamRequest, string>).FullName ?? typeof(IStreamRequestHandler<AssemblyStreamRequest, string>).Name;
        Assert.Contains(streamServiceName, exception.Message);
    }

    [Fact]
    public void AddMediora_DoesNotThrow_WhenManualRequestHandlerMatchesScannedImplementation()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IRequestHandler<AssemblyRequest, string>, AssemblyRequestHandler>();

        services.AddMediora(options => options.RegisterServicesFromAssembly(typeof(AssemblyRequestHandler).Assembly));

        using var provider = services.BuildServiceProvider();
        var handlers = provider.GetServices<IRequestHandler<AssemblyRequest, string>>();
        IRequestHandler<AssemblyRequest, string> handler = Assert.Single(handlers);
        Assert.IsType<AssemblyRequestHandler>(handler);
    }

    [Fact]
    public void AddMediora_ThrowsInvalidOperationException_WhenFactoryRegistrationConflictsWithScannedHandler()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IRequestHandler<AssemblyRequest, string>>(_ => new AssemblyRequestHandler());

        var exception = Assert.Throws<InvalidOperationException>(() => services.AddMediora(options =>
            options.RegisterServicesFromAssembly(typeof(AssemblyRequestHandler).Assembly)));

        Assert.Contains("Multiple IRequestHandler implementations", exception.Message);
        Assert.Contains("ImplementationFactory", exception.Message);
    }

    [Fact]
    public void AddMediora_ThrowsInvalidOperationException_WhenFactoryRegistrationConflictsWithManualTypeRegistration()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IRequestHandler<AssemblyRequest, string>>(_ => new AssemblyRequestHandler());
        services.AddSingleton<IRequestHandler<AssemblyRequest, string>, AssemblyRequestHandler>();

        var exception = Assert.Throws<InvalidOperationException>(() => services.AddMediora(options =>
            options.RegisterServicesFromAssembly(typeof(IdempotentRequestHandler).Assembly)));

        Assert.Contains("Multiple IRequestHandler implementations", exception.Message);
        Assert.Contains("ImplementationFactory", exception.Message);
    }

    private sealed record AssemblyRequest : IRequest<string>;

    private sealed class AssemblyRequestHandler : IRequestHandler<AssemblyRequest, string>
    {
        public Task<string> Handle(AssemblyRequest request, CancellationToken cancellationToken)
        {
            return Task.FromResult("assembly-handler");
        }
    }

    private sealed record EndToEndRequest(string Value) : IRequest<string>;

    private sealed class EndToEndHandler : IRequestHandler<EndToEndRequest, string>
    {
        public Task<string> Handle(EndToEndRequest request, CancellationToken cancellationToken)
        {
            return Task.FromResult(request.Value.ToUpperInvariant());
        }
    }

    private sealed record AssemblyStreamRequest(string Value) : IStreamRequest<string>;

    private sealed class AssemblyStreamRequestHandler : IStreamRequestHandler<AssemblyStreamRequest, string>
    {
        public async IAsyncEnumerable<string> Handle(AssemblyStreamRequest request, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return request.Value;
            await Task.Yield();
            yield return request.Value.ToUpperInvariant();
        }
    }

    private sealed record DuplicateStreamRequest : IStreamRequest<string>;

    private sealed class ManualConflictRequestHandler
    {
    }

    private sealed class ManualConflictStreamHandler
    {
    }

    private sealed record IdempotentRequest : IRequest<string>;

    private sealed class IdempotentRequestHandler : IRequestHandler<IdempotentRequest, string>
    {
        public Task<string> Handle(IdempotentRequest request, CancellationToken cancellationToken)
        {
            return Task.FromResult("ok");
        }
    }

    private sealed record IdempotentNotification : INotification;

    private sealed record UnconfiguredNotification : INotification;

    private sealed class IdempotentNotificationHandler : INotificationHandler<IdempotentNotification>
    {
        public Task Handle(IdempotentNotification notification, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class IdempotentBehavior : IPipelineBehavior<IdempotentRequest, string>
    {
        public Task<string> Handle(IdempotentRequest request, RequestHandlerDelegate<string> next, CancellationToken cancellationToken)
        {
            return next();
        }
    }

    private sealed class IdempotentStreamBehavior : IStreamPipelineBehavior<AssemblyStreamRequest, string>
    {
        public async IAsyncEnumerable<string> Handle(AssemblyStreamRequest request, StreamHandlerDelegate<string> next, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await foreach (string item in next().WithCancellation(cancellationToken))
            {
                yield return item;
            }
        }
    }

    private sealed class InvalidOpenRequestBehavior<TLeft, TRight>
    {
    }

    private sealed class InvalidOpenStreamBehavior<TLeft, TRight>
    {
    }

    private sealed class DeduplicatedOpenBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
        where TRequest : IRequest<TResponse>
    {
        public Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
            => next();
    }

    private abstract class AbstractClosedBehavior : IPipelineBehavior<IdempotentRequest, string>
    {
        public abstract Task<string> Handle(IdempotentRequest request, RequestHandlerDelegate<string> next, CancellationToken cancellationToken);
    }

    private sealed class DeduplicatedOpenStreamBehavior<TRequest, TResponse> : IStreamPipelineBehavior<TRequest, TResponse>
        where TRequest : IStreamRequest<TResponse>
    {
        public async IAsyncEnumerable<TResponse> Handle(TRequest request, StreamHandlerDelegate<TResponse> next, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await foreach (TResponse item in next().WithCancellation(cancellationToken))
            {
                yield return item;
            }
        }
    }

    private abstract class AbstractClosedStreamBehavior : IStreamPipelineBehavior<AssemblyStreamRequest, string>
    {
        public abstract IAsyncEnumerable<string> Handle(AssemblyStreamRequest request, StreamHandlerDelegate<string> next, CancellationToken cancellationToken);
    }

    private sealed class ReflectionTypeLoadExceptionAssembly : Assembly
    {
        public override Type[] GetTypes()
        {
            throw new ReflectionTypeLoadException(
                [typeof(AssemblyRequestHandler), null!],
                [new TypeLoadException("simulated loader failure")]);
        }
    }
}
