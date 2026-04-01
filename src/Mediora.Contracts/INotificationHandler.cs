namespace Mediora;

/// <summary>
/// Handles a notification of type <typeparamref name="TNotification"/>.
/// </summary>
/// <typeparam name="TNotification">The notification type.</typeparam>
public interface INotificationHandler<in TNotification>
    where TNotification : INotification
{
    /// <summary>
    /// Handles a notification.
    /// </summary>
    /// <param name="notification">The notification instance.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when handling has finished.</returns>
    Task Handle(TNotification notification, CancellationToken cancellationToken);
}
