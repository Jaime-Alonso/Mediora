namespace Mediora.Internal;

/// <summary>
/// Defines a non-generic adapter for invoking stream request handlers.
/// </summary>
internal interface IStreamRequestHandlerWrapper<TResponse>
{
    IAsyncEnumerable<TResponse> Handle(IStreamRequest<TResponse> request, IServiceProvider serviceProvider, CancellationToken cancellationToken);
}
