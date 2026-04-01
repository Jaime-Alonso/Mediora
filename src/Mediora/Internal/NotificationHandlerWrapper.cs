namespace Mediora.Internal;

/// <summary>
/// Dispatches notifications to all registered handlers for a concrete notification type.
/// </summary>
internal sealed class NotificationHandlerWrapper<TNotification> : INotificationHandlerWrapper
    where TNotification : INotification
{
    public Task Handle(INotification notification, IServiceProvider serviceProvider, CancellationToken cancellationToken)
    {
        NotificationPublishOptions publishOptions = serviceProvider.GetService(typeof(NotificationPublishOptions)) as NotificationPublishOptions
            ?? NotificationPublishOptions.Default;

        // MS.DI resolves IEnumerable<T> as an empty collection when no services are registered.
        // Keep this fallback for compatibility with non-standard IServiceProvider implementations.
        IEnumerable<INotificationHandler<TNotification>> handlers = serviceProvider.GetService(typeof(IEnumerable<INotificationHandler<TNotification>>)) as IEnumerable<INotificationHandler<TNotification>>
            ?? [];

        NotificationPublishMode mode = publishOptions.Resolve(typeof(TNotification));

        if (handlers is INotificationHandler<TNotification>[] handlerArray)
        {
            if (handlerArray.Length == 0)
            {
                return Task.CompletedTask;
            }

            return mode switch
            {
                NotificationPublishMode.SequentialFailFast => PublishSequentiallyFailFast((TNotification)notification, handlerArray, cancellationToken),
                NotificationPublishMode.SequentialAggregateAll => PublishSequentiallyAggregateAll((TNotification)notification, handlerArray, cancellationToken),
                NotificationPublishMode.ParallelAggregateAll => PublishInParallelAggregateAll((TNotification)notification, handlerArray, publishOptions.ParallelMaxDegreeOfParallelism, cancellationToken),
                _ => throw new InvalidOperationException($"Unsupported notification publish mode '{mode}'.")
            };
        }

        IList<INotificationHandler<TNotification>> handlerList = handlers as IList<INotificationHandler<TNotification>> ?? [.. handlers];

        if (handlerList.Count == 0)
        {
            return Task.CompletedTask;
        }

        return mode switch
        {
            NotificationPublishMode.SequentialFailFast => PublishSequentiallyFailFast((TNotification)notification, handlerList, cancellationToken),
            NotificationPublishMode.SequentialAggregateAll => PublishSequentiallyAggregateAll((TNotification)notification, handlerList, cancellationToken),
            NotificationPublishMode.ParallelAggregateAll => PublishInParallelAggregateAll((TNotification)notification, handlerList, publishOptions.ParallelMaxDegreeOfParallelism, cancellationToken),
            _ => throw new InvalidOperationException($"Unsupported notification publish mode '{mode}'.")
        };
    }

    private static async Task PublishSequentiallyFailFast(
        TNotification notification,
        IList<INotificationHandler<TNotification>> handlers,
        CancellationToken cancellationToken)
    {
        for (int i = 0; i < handlers.Count; i++)
        {
            await handlers[i].Handle(notification, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task PublishSequentiallyAggregateAll(
        TNotification notification,
        IList<INotificationHandler<TNotification>> handlers,
        CancellationToken cancellationToken)
    {
        List<Exception>? exceptions = null;

        for (int i = 0; i < handlers.Count; i++)
        {
            try
            {
                await handlers[i].Handle(notification, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                exceptions ??= [];
                exceptions.Add(exception);
            }
        }

        if (exceptions is not null)
        {
            throw new AggregateException(
                $"One or more notification handlers failed for notification '{typeof(TNotification).FullName}'.",
                exceptions);
        }
    }

    private static async Task PublishInParallelAggregateAll(
        TNotification notification,
        IList<INotificationHandler<TNotification>> handlers,
        int? maxDegreeOfParallelism,
        CancellationToken cancellationToken)
    {
        Task<Exception?>[] tasks = new Task<Exception?>[handlers.Count];
        Exception?[] results;

        if (maxDegreeOfParallelism is null)
        {
            for (int i = 0; i < handlers.Count; i++)
            {
                tasks[i] = ExecuteHandler(handlers[i], notification, cancellationToken);
            }

            results = await Task.WhenAll(tasks).ConfigureAwait(false);
        }
        else
        {
            using SemaphoreSlim gate = new(maxDegreeOfParallelism.Value, maxDegreeOfParallelism.Value);

            for (int i = 0; i < handlers.Count; i++)
            {
                tasks[i] = ExecuteHandlerWithLimit(handlers[i], notification, gate, cancellationToken);
            }

            results = await Task.WhenAll(tasks).ConfigureAwait(false);
        }

        List<Exception>? exceptions = null;

        for (int i = 0; i < results.Length; i++)
        {
            if (results[i] is null)
            {
                continue;
            }

            exceptions ??= [];
            exceptions.Add(results[i]!);
        }

        if (exceptions is not null)
        {
            throw new AggregateException(
                $"One or more notification handlers failed for notification '{typeof(TNotification).FullName}'.",
                exceptions);
        }
    }

    private static async Task<Exception?> ExecuteHandler(
        INotificationHandler<TNotification> handler,
        TNotification notification,
        CancellationToken cancellationToken)
    {
        try
        {
            await handler.Handle(notification, cancellationToken).ConfigureAwait(false);
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    private static async Task<Exception?> ExecuteHandlerWithLimit(
        INotificationHandler<TNotification> handler,
        TNotification notification,
        SemaphoreSlim gate,
        CancellationToken cancellationToken)
    {
        try
        {
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            return exception;
        }

        try
        {
            await handler.Handle(notification, cancellationToken).ConfigureAwait(false);
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
        finally
        {
            gate.Release();
        }
    }
}
