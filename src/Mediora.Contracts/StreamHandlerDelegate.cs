namespace Mediora;

/// <summary>
/// Delegate that represents the next step in a stream handling pipeline.
/// </summary>
/// <typeparam name="TResponse">The response type produced by the stream pipeline.</typeparam>
public delegate IAsyncEnumerable<TResponse> StreamHandlerDelegate<TResponse>();
