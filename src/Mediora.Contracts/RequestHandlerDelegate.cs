namespace Mediora;

/// <summary>
/// Delegate that represents the next step in a request handling pipeline.
/// </summary>
/// <typeparam name="TResponse">The response type produced by the pipeline.</typeparam>
public delegate Task<TResponse> RequestHandlerDelegate<TResponse>();
