namespace Mediora;

/// <summary>
/// Defines a pipeline behavior that can run before and after the stream request handler.
/// </summary>
/// <typeparam name="TRequest">The stream request type.</typeparam>
/// <typeparam name="TResponse">The stream response type.</typeparam>
public interface IStreamPipelineBehavior<in TRequest, TResponse>
    where TRequest : IStreamRequest<TResponse>
{
    /// <summary>
    /// Handles the stream request and can invoke the next delegate in the stream pipeline.
    /// </summary>
    /// <param name="request">The stream request instance.</param>
    /// <param name="next">The next delegate in the stream pipeline.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>An asynchronous stream of responses.</returns>
    IAsyncEnumerable<TResponse> Handle(TRequest request, StreamHandlerDelegate<TResponse> next, CancellationToken cancellationToken);
}
