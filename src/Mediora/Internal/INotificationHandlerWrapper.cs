namespace Mediora.Internal;

/// <summary>
/// Defines a non-generic adapter for invoking notification handlers.
/// </summary>
internal interface INotificationHandlerWrapper
{
    Task Handle(INotification notification, IServiceProvider serviceProvider, CancellationToken cancellationToken);
}
