namespace Mediora.Internal;

/// <summary>
/// Defines a non-generic adapter for invoking request handlers.
/// </summary>
internal interface IRequestHandlerWrapper<TResponse>
{
    Task<TResponse> Handle(IRequest<TResponse> request, IServiceProvider serviceProvider, CancellationToken cancellationToken);
}
