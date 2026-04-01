using System.Linq.Expressions;
using System.Reflection;
using Mediora.Internal;
using Microsoft.Extensions.DependencyInjection;

namespace Mediora;

/// <summary>
/// Default runtime implementation for request dispatching and notification publishing.
/// </summary>
public sealed class Mediator : IMediator
{
    private readonly IServiceProvider _serviceProvider;
    private readonly MediatorCacheStore _cacheStore;

    /// <summary>
    /// Initializes a new instance of the <see cref="Mediator"/> class.
    /// </summary>
    /// <param name="serviceProvider">The service provider used to resolve handlers and behaviors.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="serviceProvider"/> is <see langword="null"/>.</exception>
    public Mediator(IServiceProvider serviceProvider)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);
        _serviceProvider = serviceProvider;
        _cacheStore = serviceProvider.GetService<MediatorCacheStore>()
            ?? new MediatorCacheStore(MediatorRuntimeCacheOptions.Default);
    }

    /// <inheritdoc/>
    public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        Type requestType = request.GetType();
        IRequestHandlerWrapper<TResponse> wrapper = (IRequestHandlerWrapper<TResponse>)_cacheStore.GetOrAddRequestWrapper(
            requestType,
            typeof(TResponse),
            CreateRequestWrapper);

        return wrapper.Handle(request, _serviceProvider, cancellationToken);
    }

    /// <inheritdoc/>
    public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        Type requestType = request.GetType();
        IStreamRequestHandlerWrapper<TResponse> wrapper = (IStreamRequestHandlerWrapper<TResponse>)_cacheStore.GetOrAddStreamRequestWrapper(
            requestType,
            typeof(TResponse),
            CreateStreamRequestWrapper);

        return wrapper.Handle(request, _serviceProvider, cancellationToken);
    }

    /// <inheritdoc/>
    public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
        where TNotification : INotification
    {
        ArgumentNullException.ThrowIfNull(notification);

        INotificationHandlerWrapper wrapper = (INotificationHandlerWrapper)_cacheStore.GetOrAddNotificationWrapper(
            typeof(TNotification),
            CreateNotificationWrapper);

        return wrapper.Handle(notification, _serviceProvider, cancellationToken);
    }

    /// <inheritdoc/>
    public Task Publish(INotification notification, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(notification);

        Type notificationType = notification.GetType();
        INotificationHandlerWrapper wrapper = (INotificationHandlerWrapper)_cacheStore.GetOrAddNotificationWrapper(
            notificationType,
            CreateNotificationWrapper);

        return wrapper.Handle(notification, _serviceProvider, cancellationToken);
    }

    internal void ClearCaches() => _cacheStore.Clear();

    private object CreateRequestWrapper(Type requestType, Type responseType)
    {
        Type wrapperType = typeof(RequestHandlerWrapper<,>).MakeGenericType(requestType, responseType);
        return CreateWrapperInstance(
            wrapperType,
            $"Mediator: Failed to create request handler wrapper for request type '{requestType.FullName}' and response type '{responseType.FullName}'.");
    }

    private object CreateNotificationWrapper(Type notificationType)
    {
        Type wrapperType = typeof(NotificationHandlerWrapper<>).MakeGenericType(notificationType);
        return CreateWrapperInstance(
            wrapperType,
            $"Mediator: Failed to create notification handler wrapper for notification type '{notificationType.FullName}'.");
    }

    private object CreateStreamRequestWrapper(Type requestType, Type responseType)
    {
        Type wrapperType = typeof(StreamRequestHandlerWrapper<,>).MakeGenericType(requestType, responseType);
        return CreateWrapperInstance(
            wrapperType,
            $"Mediator: Failed to create stream request handler wrapper for request type '{requestType.FullName}' and response type '{responseType.FullName}'.");
    }

    private object CreateWrapperInstance(Type wrapperType, string errorMessage)
    {
        Func<object> factory = _cacheStore.GetOrAddWrapperFactory(
            wrapperType,
            BuildFactory);

        object instance = factory();

        return instance ?? throw new InvalidOperationException(errorMessage);
    }

    private static Func<object> BuildFactory(Type wrapperType)
    {
        ConstructorInfo? constructor = wrapperType.GetConstructor(Type.EmptyTypes)
            ?? throw new InvalidOperationException($"Mediator: Wrapper type '{wrapperType.FullName}' must define a public parameterless constructor.");

        NewExpression newExpression = Expression.New(constructor);
        UnaryExpression boxedExpression = Expression.Convert(newExpression, typeof(object));
        return Expression.Lambda<Func<object>>(boxedExpression).Compile();
    }
}
