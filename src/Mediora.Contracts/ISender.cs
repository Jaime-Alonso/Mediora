namespace Mediora;

/// <summary>
/// Sends request messages and returns responses.
/// </summary>
public interface ISender
{
    /// <summary>
    /// Sends a request to its handler.
    /// </summary>
    /// <typeparam name="TResponse">The response type.</typeparam>
    /// <param name="request">The request instance.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that resolves to the handler response.</returns>
    /// <example>
    /// <code>
    /// var response = await sender.Send(new GetOrderQuery(orderId), cancellationToken);
    /// </code>
    /// </example>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="request"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">Thrown when a handler is not registered for the request type.</exception>
    Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a stream request to its handler and returns an asynchronous response stream.
    /// </summary>
    /// <typeparam name="TResponse">The stream response type.</typeparam>
    /// <param name="request">The stream request instance.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>An asynchronous stream of handler responses.</returns>
    /// <example>
    /// <code>
    /// await foreach (var item in sender.CreateStream(new GetOrdersStreamQuery(), cancellationToken))
    /// {
    ///     // consume item
    /// }
    /// </code>
    /// </example>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="request"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">Thrown when a stream handler is not registered for the request type.</exception>
    IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default);
}
