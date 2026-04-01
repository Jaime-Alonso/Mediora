namespace Mediora;

/// <summary>
/// Publishes notification messages to zero or more handlers.
/// </summary>
public interface IPublisher
{
    /// <summary>
    /// Publishes a notification message using its compile-time notification type.
    /// </summary>
    /// <typeparam name="TNotification">The notification type.</typeparam>
    /// <param name="notification">The notification instance.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when all handlers finish.</returns>
    /// <example>
    /// <code>
    /// await publisher.Publish(new OrderCreatedNotification(orderId), cancellationToken);
    /// </code>
    /// </example>
    /// <remarks>
    /// If no handlers are registered for the given notification type,
    /// the method completes successfully without executing any handler.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="notification"/> is <see langword="null"/>.</exception>
    /// <exception cref="AggregateException">Thrown when one or more notification handlers fail.</exception>
    Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
        where TNotification : INotification;

    /// <summary>
    /// Publishes a notification message.
    /// </summary>
    /// <param name="notification">The notification instance.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when all handlers finish.</returns>
    /// <remarks>
    /// If no handlers are registered for the given notification type,
    /// the method completes successfully without executing any handler.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="notification"/> is <see langword="null"/>.</exception>
    /// <exception cref="AggregateException">Thrown when one or more notification handlers fail.</exception>
    Task Publish(INotification notification, CancellationToken cancellationToken = default);
}
