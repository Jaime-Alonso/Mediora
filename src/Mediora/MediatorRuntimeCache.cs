using System.Collections.Concurrent;
using System.Threading;

namespace Mediora;

internal sealed record MediatorRuntimeCacheOptions(
    bool EnableCaching,
    int MaxRequestWrappers,
    int MaxStreamRequestWrappers,
    int MaxNotificationWrappers,
    int MaxWrapperFactories,
    TimeSpan? SlidingExpiration,
    TimeSpan? AbsoluteExpiration)
{
    internal static readonly MediatorRuntimeCacheOptions Default = new(
        true,
        2048,
        1024,
        2048,
        2048,
        null,
        null);
}

internal sealed class MediatorCacheStore
{
    private readonly MediatorRuntimeCacheOptions _options;
    private readonly BoundedExpiringCache<(Type RequestType, Type ResponseType), object> _requestWrappers;
    private readonly BoundedExpiringCache<(Type RequestType, Type ResponseType), object> _streamRequestWrappers;
    private readonly BoundedExpiringCache<Type, object> _notificationWrappers;
    private readonly BoundedExpiringCache<Type, Func<object>> _wrapperFactories;

    internal MediatorCacheStore(MediatorRuntimeCacheOptions options)
    {
        _options = options;
        _requestWrappers = new BoundedExpiringCache<(Type RequestType, Type ResponseType), object>(
            options.MaxRequestWrappers,
            options.SlidingExpiration,
            options.AbsoluteExpiration);
        _streamRequestWrappers = new BoundedExpiringCache<(Type RequestType, Type ResponseType), object>(
            options.MaxStreamRequestWrappers,
            options.SlidingExpiration,
            options.AbsoluteExpiration);
        _notificationWrappers = new BoundedExpiringCache<Type, object>(
            options.MaxNotificationWrappers,
            options.SlidingExpiration,
            options.AbsoluteExpiration);
        _wrapperFactories = new BoundedExpiringCache<Type, Func<object>>(
            options.MaxWrapperFactories,
            options.SlidingExpiration,
            options.AbsoluteExpiration);
    }

    internal object GetOrAddRequestWrapper(Type requestType, Type responseType, Func<Type, Type, object> factory)
    {
        if (!_options.EnableCaching)
        {
            return factory(requestType, responseType);
        }

        return _requestWrappers.GetOrAdd((requestType, responseType), key => factory(key.RequestType, key.ResponseType));
    }

    internal object GetOrAddStreamRequestWrapper(Type requestType, Type responseType, Func<Type, Type, object> factory)
    {
        if (!_options.EnableCaching)
        {
            return factory(requestType, responseType);
        }

        return _streamRequestWrappers.GetOrAdd((requestType, responseType), key => factory(key.RequestType, key.ResponseType));
    }

    internal object GetOrAddNotificationWrapper(Type notificationType, Func<Type, object> factory)
    {
        if (!_options.EnableCaching)
        {
            return factory(notificationType);
        }

        return _notificationWrappers.GetOrAdd(notificationType, factory);
    }

    internal Func<object> GetOrAddWrapperFactory(Type wrapperType, Func<Type, Func<object>> factory)
    {
        if (!_options.EnableCaching)
        {
            return factory(wrapperType);
        }

        return _wrapperFactories.GetOrAdd(wrapperType, factory);
    }

    internal int RequestWrapperCount => _requestWrappers.Count;

    internal int StreamRequestWrapperCount => _streamRequestWrappers.Count;

    internal int NotificationWrapperCount => _notificationWrappers.Count;

    internal int WrapperFactoryCount => _wrapperFactories.Count;

    internal void Clear()
    {
        _requestWrappers.Clear();
        _streamRequestWrappers.Clear();
        _notificationWrappers.Clear();
        _wrapperFactories.Clear();
    }

    private sealed class BoundedExpiringCache<TKey, TValue>
        where TKey : notnull
    {
        private readonly ConcurrentDictionary<TKey, CacheEntry> _entries = new();
        private readonly ConcurrentQueue<TKey> _insertionOrder = new();
        private readonly int _maxSize;
        private readonly TimeSpan? _slidingExpiration;
        private readonly TimeSpan? _absoluteExpiration;

        internal BoundedExpiringCache(int maxSize, TimeSpan? slidingExpiration, TimeSpan? absoluteExpiration)
        {
            _maxSize = maxSize;
            _slidingExpiration = slidingExpiration;
            _absoluteExpiration = absoluteExpiration;
        }

        internal int Count => _entries.Count;

        internal TValue GetOrAdd(TKey key, Func<TKey, TValue> valueFactory)
        {
            while (true)
            {
                long nowTicks = DateTime.UtcNow.Ticks;

                if (_entries.TryGetValue(key, out CacheEntry? existing))
                {
                    if (existing.IsExpired(nowTicks, _slidingExpiration, _absoluteExpiration))
                    {
                        _entries.TryRemove(key, out _);
                        continue;
                    }

                    existing.Touch(nowTicks);
                    return existing.Value;
                }

                TValue value = valueFactory(key);
                CacheEntry created = new(value, nowTicks);

                if (_entries.TryAdd(key, created))
                {
                    _insertionOrder.Enqueue(key);
                    Trim(nowTicks);
                    return value;
                }
            }
        }

        internal void Clear()
        {
            _entries.Clear();

            while (_insertionOrder.TryDequeue(out _))
            {
            }
        }

        private void Trim(long nowTicks)
        {
            while (_entries.Count > _maxSize && _insertionOrder.TryDequeue(out TKey? oldestKey))
            {
                if (!_entries.TryGetValue(oldestKey, out CacheEntry? candidate))
                {
                    continue;
                }

                if (candidate.IsExpired(nowTicks, _slidingExpiration, _absoluteExpiration)
                    || _entries.Count > _maxSize)
                {
                    _entries.TryRemove(oldestKey, out _);
                }
            }
        }

        private sealed class CacheEntry
        {
            private readonly long _createdUtcTicks;
            private long _lastAccessUtcTicks;

            internal CacheEntry(TValue value, long nowTicks)
            {
                Value = value;
                _createdUtcTicks = nowTicks;
                _lastAccessUtcTicks = nowTicks;
            }

            internal TValue Value { get; }

            internal void Touch(long nowTicks)
            {
                Interlocked.Exchange(ref _lastAccessUtcTicks, nowTicks);
            }

            internal bool IsExpired(long nowTicks, TimeSpan? slidingExpiration, TimeSpan? absoluteExpiration)
            {
                if (absoluteExpiration is not null && nowTicks - _createdUtcTicks >= absoluteExpiration.Value.Ticks)
                {
                    return true;
                }

                if (slidingExpiration is not null)
                {
                    long lastAccessTicks = Interlocked.Read(ref _lastAccessUtcTicks);
                    if (nowTicks - lastAccessTicks >= slidingExpiration.Value.Ticks)
                    {
                        return true;
                    }
                }

                return false;
            }
        }
    }
}
