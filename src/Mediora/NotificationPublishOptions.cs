namespace Mediora;

internal sealed class NotificationPublishOptions
{
    private readonly Dictionary<Type, NotificationPublishMode> _modeByNotificationType;

    internal NotificationPublishOptions(
        NotificationPublishMode defaultMode,
        int? parallelMaxDegreeOfParallelism,
        Dictionary<Type, NotificationPublishMode> modeByNotificationType)
    {
        DefaultMode = defaultMode;
        ParallelMaxDegreeOfParallelism = parallelMaxDegreeOfParallelism;
        _modeByNotificationType = modeByNotificationType;
    }

    internal static readonly NotificationPublishOptions Default = new(
        NotificationPublishMode.SequentialFailFast,
        null,
        []);

    internal NotificationPublishMode DefaultMode { get; }

    internal int? ParallelMaxDegreeOfParallelism { get; }

    internal NotificationPublishMode Resolve(Type notificationType)
    {
        ArgumentNullException.ThrowIfNull(notificationType);

        if (_modeByNotificationType.TryGetValue(notificationType, out NotificationPublishMode mode))
        {
            return mode;
        }

        return DefaultMode;
    }
}
