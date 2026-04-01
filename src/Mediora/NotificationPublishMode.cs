namespace Mediora;

/// <summary>
/// Defines how notification handlers are executed when publishing a notification.
/// </summary>
public enum NotificationPublishMode
{
    /// <summary>
    /// Executes handlers one by one in registration order and stops on the first failure.
    /// </summary>
    SequentialFailFast = 0,

    /// <summary>
    /// Executes handlers one by one in registration order and aggregates failures.
    /// </summary>
    SequentialAggregateAll = 1,

    /// <summary>
    /// Executes handlers concurrently and aggregates failures.
    /// </summary>
    ParallelAggregateAll = 2
}
