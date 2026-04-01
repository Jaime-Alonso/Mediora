using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Mediora;

/// <summary>
/// Extension methods for registering Mediora services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds Mediora, discovered handlers, and configured behaviors to the service collection.
    /// </summary>
    /// <param name="services">The target service collection.</param>
    /// <param name="configure">A callback used to configure assembly scanning and options.</param>
    /// <returns>The same <paramref name="services"/> instance.</returns>
    /// <example>
    /// <code>
    /// services.AddMediora(options =&gt;
    /// {
    ///     options.RegisterServicesFromAssembly(typeof(CreateOrderHandler).Assembly);
    /// });
    /// </code>
    /// </example>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/> or <paramref name="configure"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">Thrown when no assemblies are registered, or duplicate single-handler contracts are detected.</exception>
    public static IServiceCollection AddMediora(this IServiceCollection services, Action<MedioraServiceConfiguration> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        MedioraServiceConfiguration options = new();
        configure(options);

        if (options.Assemblies.Count == 0)
        {
            throw new InvalidOperationException("No assemblies were configured for Mediora scanning. Call RegisterServicesFromAssembly or RegisterServicesFromAssemblies.");
        }

        services.TryAddSingleton(new MediatorCacheStore(options.GetRuntimeCacheOptions()));
        services.TryAddSingleton(options.GetNotificationPublishOptions());

        services.TryAdd(new ServiceDescriptor(typeof(IMediator), typeof(Mediator), options.Lifetime));
        services.TryAdd(new ServiceDescriptor(typeof(ISender), typeof(Mediator), options.Lifetime));
        services.TryAdd(new ServiceDescriptor(typeof(IPublisher), typeof(Mediator), options.Lifetime));

        IReadOnlyList<MedioraServiceConfiguration.ServiceRegistration> registrations = options.GetServiceRegistrations();
        ValidateRequestHandlerUniqueness(services, registrations);

        for (int i = 0; i < registrations.Count; i++)
        {
            MedioraServiceConfiguration.ServiceRegistration registration = registrations[i];

            if (registration.ContractType == typeof(IRequestHandler<,>)
                || registration.ContractType == typeof(IRequestHandler<>)
                || registration.ContractType == typeof(IStreamRequestHandler<,>))
            {
                services.TryAdd(new ServiceDescriptor(registration.ServiceType, registration.ImplementationType, options.Lifetime));
                continue;
            }

            if (registration.ContractType == typeof(INotificationHandler<>))
            {
                services.TryAddEnumerable(new ServiceDescriptor(registration.ServiceType, registration.ImplementationType, options.Lifetime));
            }
        }

        IReadOnlyList<MedioraServiceConfiguration.EnumerableRegistration> pipelineBehaviors = options.PipelineBehaviorRegistrations;
        for (int i = 0; i < pipelineBehaviors.Count; i++)
        {
            MedioraServiceConfiguration.EnumerableRegistration registration = pipelineBehaviors[i];
            services.TryAddEnumerable(new ServiceDescriptor(registration.ServiceType, registration.ImplementationType, options.Lifetime));
        }

        IReadOnlyList<MedioraServiceConfiguration.EnumerableRegistration> streamBehaviors = options.StreamPipelineBehaviorRegistrations;
        for (int i = 0; i < streamBehaviors.Count; i++)
        {
            MedioraServiceConfiguration.EnumerableRegistration registration = streamBehaviors[i];
            services.TryAddEnumerable(new ServiceDescriptor(registration.ServiceType, registration.ImplementationType, options.Lifetime));
        }

        return services;
    }

    private static void ValidateRequestHandlerUniqueness(
        IServiceCollection services,
        IReadOnlyList<MedioraServiceConfiguration.ServiceRegistration> registrations)
    {
        ValidateSingleHandlerUniqueness(services, registrations, typeof(IRequestHandler<,>), typeof(IRequestHandler<>), "IRequestHandler");
        ValidateSingleHandlerUniqueness(services, registrations, typeof(IStreamRequestHandler<,>), null, "IStreamRequestHandler");
    }

    private static void ValidateSingleHandlerUniqueness(
        IServiceCollection services,
        IReadOnlyList<MedioraServiceConfiguration.ServiceRegistration> registrations,
        Type primaryContractType,
        Type? secondaryContractType,
        string contractName)
    {
        Dictionary<Type, Dictionary<string, string>> requestHandlerImplementationsByService = [];

        for (int i = 0; i < services.Count; i++)
        {
            ServiceDescriptor descriptor = services[i];
            Type serviceType = descriptor.ServiceType;

            if (!IsSingleHandlerContract(serviceType, primaryContractType, secondaryContractType)
                || serviceType.ContainsGenericParameters)
            {
                continue;
            }

            if (descriptor.ImplementationType is not null)
            {
                string typeName = descriptor.ImplementationType.FullName ?? descriptor.ImplementationType.Name;
                AddImplementation(requestHandlerImplementationsByService, serviceType, $"type:{typeName}", typeName);
                continue;
            }

            if (descriptor.ImplementationInstance is not null)
            {
                Type implementationType = descriptor.ImplementationInstance.GetType();
                string typeName = implementationType.FullName ?? implementationType.Name;
                AddImplementation(requestHandlerImplementationsByService, serviceType, $"instance:{typeName}", typeName);
                continue;
            }

            if (descriptor.ImplementationFactory is not null)
            {
                string serviceName = serviceType.FullName ?? serviceType.Name;
                AddImplementation(
                    requestHandlerImplementationsByService,
                    serviceType,
                    $"factory:{serviceName}:{i}",
                    "ImplementationFactory");
            }
        }

        for (int i = 0; i < registrations.Count; i++)
        {
            MedioraServiceConfiguration.ServiceRegistration registration = registrations[i];

            if (!IsSingleHandlerContract(registration.ServiceType, primaryContractType, secondaryContractType)
                || registration.ServiceType.ContainsGenericParameters)
            {
                continue;
            }

            string typeName = registration.ImplementationType.FullName ?? registration.ImplementationType.Name;
            AddImplementation(requestHandlerImplementationsByService, registration.ServiceType, $"type:{typeName}", typeName);
        }

        List<string> duplicateMessages = [];

        foreach (KeyValuePair<Type, Dictionary<string, string>> pair in requestHandlerImplementationsByService)
        {
            if (pair.Value.Count <= 1)
            {
                continue;
            }

            duplicateMessages.Add($"{pair.Key.FullName}: {string.Join(", ", pair.Value.Values)}");
        }

        if (duplicateMessages.Count == 0)
        {
            return;
        }

        string message = $"Multiple {contractName} implementations were found for the same request contract. Each request contract must have exactly one handler. Conflicts: {string.Join("; ", duplicateMessages)}";
        throw new InvalidOperationException(message);
    }

    private static bool IsSingleHandlerContract(Type serviceType, Type primaryContractType, Type? secondaryContractType)
    {
        if (!serviceType.IsGenericType)
        {
            return false;
        }

        Type serviceTypeDefinition = serviceType.GetGenericTypeDefinition();
        return serviceTypeDefinition == primaryContractType || serviceTypeDefinition == secondaryContractType;
    }

    private static void AddImplementation(
        Dictionary<Type, Dictionary<string, string>> implementationsByService,
        Type serviceType,
        string implementationKey,
        string implementationDisplay)
    {
        if (!implementationsByService.TryGetValue(serviceType, out Dictionary<string, string>? implementations))
        {
            implementations = [];
            implementationsByService[serviceType] = implementations;
        }

        implementations.TryAdd(implementationKey, implementationDisplay);
    }
}
