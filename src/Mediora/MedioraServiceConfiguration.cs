using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace Mediora;

/// <summary>
/// Provides options for Mediora dependency injection registration.
/// </summary>
/// <remarks>
/// Assembly scan results are cached per configuration instance.
/// Wrapper cache limits and expiration can be configured for runtime dispatch.
/// </remarks>
public sealed class MedioraServiceConfiguration
{
    private readonly Dictionary<Assembly, IReadOnlyList<ServiceRegistration>> _scanCache = [];
    private readonly Dictionary<Type, NotificationPublishMode> _notificationPublishModes = [];
    private readonly List<EnumerableRegistration> _pipelineBehaviorRegistrations = [];
    private readonly HashSet<(string ServiceType, string ImplementationType)> _pipelineBehaviorRegistrationSet = [];
    private readonly List<EnumerableRegistration> _streamPipelineBehaviorRegistrations = [];
    private readonly HashSet<(string ServiceType, string ImplementationType)> _streamPipelineBehaviorRegistrationSet = [];

    private readonly List<Assembly> _assemblies = [];
    private readonly HashSet<Assembly> _assemblySet = [];

    /// <summary>
    /// Gets or sets the service lifetime used for Mediora services.
    /// </summary>
    public ServiceLifetime Lifetime { get; set; } = ServiceLifetime.Transient;

    /// <summary>
    /// Gets or sets a value indicating whether runtime wrapper caching is enabled.
    /// </summary>
    public bool EnableWrapperCaching { get; set; } = true;

    /// <summary>
    /// Gets or sets the maximum number of cached request wrappers.
    /// </summary>
    public int MaxRequestWrappers { get; set; } = 2048;

    /// <summary>
    /// Gets or sets the maximum number of cached stream request wrappers.
    /// </summary>
    public int MaxStreamRequestWrappers { get; set; } = 1024;

    /// <summary>
    /// Gets or sets the maximum number of cached notification wrappers.
    /// </summary>
    public int MaxNotificationWrappers { get; set; } = 2048;

    /// <summary>
    /// Gets or sets the maximum number of cached wrapper factory delegates.
    /// </summary>
    public int MaxWrapperFactories { get; set; } = 2048;

    /// <summary>
    /// Gets or sets the optional sliding expiration for cached wrappers and factories.
    /// </summary>
    public TimeSpan? WrapperCacheSlidingExpiration { get; set; }

    /// <summary>
    /// Gets or sets the optional absolute expiration for cached wrappers and factories.
    /// </summary>
    public TimeSpan? WrapperCacheAbsoluteExpiration { get; set; }

    /// <summary>
    /// Gets or sets the default notification publish mode.
    /// </summary>
    public NotificationPublishMode DefaultNotificationPublishMode { get; set; } = NotificationPublishMode.SequentialFailFast;

    /// <summary>
    /// Gets or sets the optional maximum degree of parallelism used in parallel notification publishing.
    /// </summary>
    public int? NotificationParallelMaxDegreeOfParallelism { get; set; }

    /// <summary>
    /// Registers an assembly to scan for Mediora handlers.
    /// </summary>
    /// <param name="assembly">The assembly to scan.</param>
    /// <returns>The current configuration instance.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="assembly"/> is <see langword="null"/>.</exception>
    public MedioraServiceConfiguration RegisterServicesFromAssembly(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        if (_assemblySet.Add(assembly))
        {
            _assemblies.Add(assembly);
        }

        return this;
    }

    /// <summary>
    /// Registers assemblies to scan for Mediora handlers.
    /// </summary>
    /// <param name="assemblies">The assemblies to scan.</param>
    /// <returns>The current configuration instance.</returns>
    public MedioraServiceConfiguration RegisterServicesFromAssemblies(params ReadOnlySpan<Assembly> assemblies)
    {
        for (int i = 0; i < assemblies.Length; i++)
        {
            RegisterServicesFromAssembly(assemblies[i]);
        }

        return this;
    }

    /// <summary>
    /// Registers a closed request behavior implementation.
    /// </summary>
    /// <typeparam name="TBehavior">The closed behavior type.</typeparam>
    /// <returns>The current configuration instance.</returns>
    public MedioraServiceConfiguration AddBehavior<TBehavior>()
        where TBehavior : class
        => AddBehavior(typeof(TBehavior));

    /// <summary>
    /// Registers a closed request behavior implementation.
    /// </summary>
    /// <param name="behaviorType">The closed behavior type.</param>
    /// <returns>The current configuration instance.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="behaviorType"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="behaviorType"/> is not a valid closed request behavior type.</exception>
    public MedioraServiceConfiguration AddBehavior(Type behaviorType)
    {
        ArgumentNullException.ThrowIfNull(behaviorType);

        if (!behaviorType.IsClass || behaviorType.IsAbstract)
        {
            throw new ArgumentException(
                $"Behavior type '{behaviorType.FullName}' must be a non-abstract class.",
                nameof(behaviorType));
        }

        if (behaviorType.IsGenericTypeDefinition)
        {
            throw new ArgumentException(
                $"Behavior type '{behaviorType.FullName}' is open generic. Use AddOpenBehavior for open generic registrations.",
                nameof(behaviorType));
        }

        Type[] interfaces = behaviorType.GetInterfaces();
        bool hasMatch = false;

        for (int i = 0; i < interfaces.Length; i++)
        {
            Type serviceType = interfaces[i];
            if (!serviceType.IsGenericType || serviceType.GetGenericTypeDefinition() != typeof(IPipelineBehavior<,>))
            {
                continue;
            }

            hasMatch = true;
            AddEnumerableRegistrationIfMissing(
                _pipelineBehaviorRegistrations,
                _pipelineBehaviorRegistrationSet,
                new EnumerableRegistration(serviceType, behaviorType));
        }

        if (!hasMatch)
        {
            throw new ArgumentException(
                $"Behavior type '{behaviorType.FullName}' must implement IPipelineBehavior<,>.",
                nameof(behaviorType));
        }

        return this;
    }

    /// <summary>
    /// Registers an open generic request behavior implementation.
    /// </summary>
    /// <param name="openBehaviorType">The open generic behavior type.</param>
    /// <returns>The current configuration instance.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="openBehaviorType"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="openBehaviorType"/> is not a valid open generic request behavior type.</exception>
    public MedioraServiceConfiguration AddOpenBehavior(Type openBehaviorType)
    {
        ArgumentNullException.ThrowIfNull(openBehaviorType);

        if (!openBehaviorType.IsClass || openBehaviorType.IsAbstract)
        {
            throw new ArgumentException(
                $"Behavior type '{openBehaviorType.FullName}' must be a non-abstract class.",
                nameof(openBehaviorType));
        }

        if (!openBehaviorType.IsGenericTypeDefinition)
        {
            throw new ArgumentException(
                $"Behavior type '{openBehaviorType.FullName}' must be an open generic type definition.",
                nameof(openBehaviorType));
        }

        if (!ImplementsOpenGenericContract(openBehaviorType, typeof(IPipelineBehavior<,>)))
        {
            throw new ArgumentException(
                $"Behavior type '{openBehaviorType.FullName}' must implement IPipelineBehavior<,>.",
                nameof(openBehaviorType));
        }

        AddEnumerableRegistrationIfMissing(
            _pipelineBehaviorRegistrations,
            _pipelineBehaviorRegistrationSet,
            new EnumerableRegistration(typeof(IPipelineBehavior<,>), openBehaviorType));

        return this;
    }

    /// <summary>
    /// Registers a closed stream behavior implementation.
    /// </summary>
    /// <typeparam name="TBehavior">The closed stream behavior type.</typeparam>
    /// <returns>The current configuration instance.</returns>
    public MedioraServiceConfiguration AddStreamBehavior<TBehavior>()
        where TBehavior : class
        => AddStreamBehavior(typeof(TBehavior));

    /// <summary>
    /// Registers a closed stream behavior implementation.
    /// </summary>
    /// <param name="behaviorType">The closed stream behavior type.</param>
    /// <returns>The current configuration instance.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="behaviorType"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="behaviorType"/> is not a valid closed stream behavior type.</exception>
    public MedioraServiceConfiguration AddStreamBehavior(Type behaviorType)
    {
        ArgumentNullException.ThrowIfNull(behaviorType);

        if (!behaviorType.IsClass || behaviorType.IsAbstract)
        {
            throw new ArgumentException(
                $"Behavior type '{behaviorType.FullName}' must be a non-abstract class.",
                nameof(behaviorType));
        }

        if (behaviorType.IsGenericTypeDefinition)
        {
            throw new ArgumentException(
                $"Behavior type '{behaviorType.FullName}' is open generic. Use AddOpenStreamBehavior for open generic registrations.",
                nameof(behaviorType));
        }

        Type[] interfaces = behaviorType.GetInterfaces();
        bool hasMatch = false;

        for (int i = 0; i < interfaces.Length; i++)
        {
            Type serviceType = interfaces[i];
            if (!serviceType.IsGenericType || serviceType.GetGenericTypeDefinition() != typeof(IStreamPipelineBehavior<,>))
            {
                continue;
            }

            hasMatch = true;
            AddEnumerableRegistrationIfMissing(
                _streamPipelineBehaviorRegistrations,
                _streamPipelineBehaviorRegistrationSet,
                new EnumerableRegistration(serviceType, behaviorType));
        }

        if (!hasMatch)
        {
            throw new ArgumentException(
                $"Behavior type '{behaviorType.FullName}' must implement IStreamPipelineBehavior<,>.",
                nameof(behaviorType));
        }

        return this;
    }

    /// <summary>
    /// Registers an open generic stream behavior implementation.
    /// </summary>
    /// <param name="openBehaviorType">The open generic stream behavior type.</param>
    /// <returns>The current configuration instance.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="openBehaviorType"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="openBehaviorType"/> is not a valid open generic stream behavior type.</exception>
    public MedioraServiceConfiguration AddOpenStreamBehavior(Type openBehaviorType)
    {
        ArgumentNullException.ThrowIfNull(openBehaviorType);

        if (!openBehaviorType.IsClass || openBehaviorType.IsAbstract)
        {
            throw new ArgumentException(
                $"Behavior type '{openBehaviorType.FullName}' must be a non-abstract class.",
                nameof(openBehaviorType));
        }

        if (!openBehaviorType.IsGenericTypeDefinition)
        {
            throw new ArgumentException(
                $"Behavior type '{openBehaviorType.FullName}' must be an open generic type definition.",
                nameof(openBehaviorType));
        }

        if (!ImplementsOpenGenericContract(openBehaviorType, typeof(IStreamPipelineBehavior<,>)))
        {
            throw new ArgumentException(
                $"Behavior type '{openBehaviorType.FullName}' must implement IStreamPipelineBehavior<,>.",
                nameof(openBehaviorType));
        }

        AddEnumerableRegistrationIfMissing(
            _streamPipelineBehaviorRegistrations,
            _streamPipelineBehaviorRegistrationSet,
            new EnumerableRegistration(typeof(IStreamPipelineBehavior<,>), openBehaviorType));

        return this;
    }

    /// <summary>
    /// Configures the publish mode for a specific notification type.
    /// </summary>
    /// <typeparam name="TNotification">The notification type.</typeparam>
    /// <param name="mode">The publish mode to apply.</param>
    /// <returns>The current configuration instance.</returns>
    public MedioraServiceConfiguration ConfigureNotificationPublishMode<TNotification>(NotificationPublishMode mode)
        where TNotification : INotification
    {
        _notificationPublishModes[typeof(TNotification)] = mode;
        return this;
    }

    /// <summary>
    /// Configures the publish mode for one or more notification types.
    /// </summary>
    /// <param name="mode">The publish mode to apply.</param>
    /// <param name="notificationTypes">Notification types to configure.</param>
    /// <returns>The current configuration instance.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="notificationTypes"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when a configured type does not implement <see cref="INotification"/>.</exception>
    public MedioraServiceConfiguration ConfigureNotificationPublishMode(NotificationPublishMode mode, params Type[] notificationTypes)
    {
        ArgumentNullException.ThrowIfNull(notificationTypes);

        for (int i = 0; i < notificationTypes.Length; i++)
        {
            Type notificationType = notificationTypes[i] ?? throw new ArgumentException("Notification types cannot contain null entries.", nameof(notificationTypes));

            if (!typeof(INotification).IsAssignableFrom(notificationType))
            {
                throw new ArgumentException(
                    $"Configured notification type '{notificationType.FullName}' must implement INotification.",
                    nameof(notificationTypes));
            }

            _notificationPublishModes[notificationType] = mode;
        }

        return this;
    }

    internal IReadOnlyList<Assembly> Assemblies => _assemblies;

    internal IReadOnlyList<EnumerableRegistration> PipelineBehaviorRegistrations => _pipelineBehaviorRegistrations;

    internal IReadOnlyList<EnumerableRegistration> StreamPipelineBehaviorRegistrations => _streamPipelineBehaviorRegistrations;

    internal void ClearScanCache()
    {
        _scanCache.Clear();
    }

    internal MediatorRuntimeCacheOptions GetRuntimeCacheOptions()
    {
        if (MaxRequestWrappers <= 0)
        {
            throw new InvalidOperationException("MaxRequestWrappers must be greater than zero.");
        }

        if (MaxStreamRequestWrappers <= 0)
        {
            throw new InvalidOperationException("MaxStreamRequestWrappers must be greater than zero.");
        }

        if (MaxNotificationWrappers <= 0)
        {
            throw new InvalidOperationException("MaxNotificationWrappers must be greater than zero.");
        }

        if (MaxWrapperFactories <= 0)
        {
            throw new InvalidOperationException("MaxWrapperFactories must be greater than zero.");
        }

        if (WrapperCacheSlidingExpiration is not null && WrapperCacheSlidingExpiration <= TimeSpan.Zero)
        {
            throw new InvalidOperationException("WrapperCacheSlidingExpiration must be greater than TimeSpan.Zero when configured.");
        }

        if (WrapperCacheAbsoluteExpiration is not null && WrapperCacheAbsoluteExpiration <= TimeSpan.Zero)
        {
            throw new InvalidOperationException("WrapperCacheAbsoluteExpiration must be greater than TimeSpan.Zero when configured.");
        }

        return new MediatorRuntimeCacheOptions(
            EnableWrapperCaching,
            MaxRequestWrappers,
            MaxStreamRequestWrappers,
            MaxNotificationWrappers,
            MaxWrapperFactories,
            WrapperCacheSlidingExpiration,
            WrapperCacheAbsoluteExpiration);
    }

    internal NotificationPublishOptions GetNotificationPublishOptions()
    {
        if (NotificationParallelMaxDegreeOfParallelism is not null
            && NotificationParallelMaxDegreeOfParallelism <= 0)
        {
            throw new InvalidOperationException("NotificationParallelMaxDegreeOfParallelism must be greater than zero when configured.");
        }

        return new NotificationPublishOptions(
            DefaultNotificationPublishMode,
            NotificationParallelMaxDegreeOfParallelism,
            new Dictionary<Type, NotificationPublishMode>(_notificationPublishModes));
    }

    internal IReadOnlyList<ServiceRegistration> GetServiceRegistrations()
    {
        List<ServiceRegistration> registrations = [];

        for (int i = 0; i < _assemblies.Count; i++)
        {
            Assembly assembly = _assemblies[i];
            if (!_scanCache.TryGetValue(assembly, out IReadOnlyList<ServiceRegistration>? assemblyRegistrations))
            {
                assemblyRegistrations = ScanAssembly(assembly);
                _scanCache[assembly] = assemblyRegistrations;
            }

            registrations.AddRange(assemblyRegistrations);
        }

        return registrations;
    }

    private static List<ServiceRegistration> ScanAssembly(Assembly assembly)
    {
        List<ServiceRegistration> registrations = [];
        HashSet<(string ServiceType, string ImplementationType)> seen = [];
        Type[] types = GetLoadableTypes(assembly);

        for (int i = 0; i < types.Length; i++)
        {
            Type implementationType = types[i];

            if (implementationType is null || implementationType.IsAbstract || implementationType.IsInterface)
            {
                continue;
            }

            Type[] interfaces = implementationType.GetInterfaces();

            for (int j = 0; j < interfaces.Length; j++)
            {
                Type serviceType = interfaces[j];

                if (!serviceType.IsGenericType)
                {
                    continue;
                }

                Type serviceTypeDefinition = serviceType.GetGenericTypeDefinition();

                if (serviceTypeDefinition == typeof(IRequestHandler<,>))
                {
                    if (!serviceType.ContainsGenericParameters && !implementationType.ContainsGenericParameters)
                    {
                        AddIfMissing(registrations, seen, new ServiceRegistration(serviceType, implementationType, serviceTypeDefinition));
                    }

                    continue;
                }

                if (serviceTypeDefinition == typeof(IRequestHandler<>))
                {
                    if (!serviceType.ContainsGenericParameters && !implementationType.ContainsGenericParameters)
                    {
                        AddIfMissing(registrations, seen, new ServiceRegistration(serviceType, implementationType, serviceTypeDefinition));
                    }

                    continue;
                }

                if (serviceTypeDefinition == typeof(INotificationHandler<>))
                {
                    if (!serviceType.ContainsGenericParameters && !implementationType.ContainsGenericParameters)
                    {
                        AddIfMissing(registrations, seen, new ServiceRegistration(serviceType, implementationType, serviceTypeDefinition));
                    }

                    continue;
                }

                if (serviceTypeDefinition == typeof(IStreamRequestHandler<,>))
                {
                    if (!serviceType.ContainsGenericParameters && !implementationType.ContainsGenericParameters)
                    {
                        AddIfMissing(registrations, seen, new ServiceRegistration(serviceType, implementationType, serviceTypeDefinition));
                    }

                    continue;
                }

            }
        }

        return registrations;
    }

    private static Type[] GetLoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException exception)
        {
            List<Type> types = [];

            for (int i = 0; i < exception.Types.Length; i++)
            {
                Type? type = exception.Types[i];

                if (type is not null)
                {
                    types.Add(type);
                }
            }

            return [.. types];
        }
    }

    private static void AddIfMissing(List<ServiceRegistration> registrations, HashSet<(string ServiceType, string ImplementationType)> seen, ServiceRegistration registration)
    {
        string serviceType = registration.ServiceType.FullName ?? registration.ServiceType.Name;
        string implementationType = registration.ImplementationType.FullName ?? registration.ImplementationType.Name;

        if (seen.Add((serviceType, implementationType)))
        {
            registrations.Add(registration);
        }
    }

    private static bool ImplementsOpenGenericContract(Type implementationType, Type contractType)
    {
        Type[] interfaces = implementationType.GetInterfaces();
        for (int i = 0; i < interfaces.Length; i++)
        {
            Type serviceType = interfaces[i];
            if (!serviceType.IsGenericType)
            {
                continue;
            }

            if (serviceType.GetGenericTypeDefinition() == contractType)
            {
                return true;
            }
        }

        return false;
    }

    private static void AddEnumerableRegistrationIfMissing(
        List<EnumerableRegistration> registrations,
        HashSet<(string ServiceType, string ImplementationType)> seen,
        EnumerableRegistration registration)
    {
        string serviceType = registration.ServiceType.FullName ?? registration.ServiceType.Name;
        string implementationType = registration.ImplementationType.FullName ?? registration.ImplementationType.Name;

        if (seen.Add((serviceType, implementationType)))
        {
            registrations.Add(registration);
        }
    }

    internal readonly record struct ServiceRegistration(Type ServiceType, Type ImplementationType, Type ContractType);

    internal readonly record struct EnumerableRegistration(Type ServiceType, Type ImplementationType);
}
