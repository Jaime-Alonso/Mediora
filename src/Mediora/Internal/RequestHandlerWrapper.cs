using Microsoft.Extensions.DependencyInjection;

namespace Mediora.Internal;

/// <summary>
/// Resolves and invokes the handler pipeline for a concrete request/response pair.
/// </summary>
internal sealed class RequestHandlerWrapper<TRequest, TResponse> : IRequestHandlerWrapper<TResponse>
    where TRequest : IRequest<TResponse>
{
    public Task<TResponse> Handle(IRequest<TResponse> request, IServiceProvider serviceProvider, CancellationToken cancellationToken)
    {
        Type handlerType = typeof(IRequestHandler<TRequest, TResponse>);

        if (serviceProvider.GetService(handlerType) is not IRequestHandler<TRequest, TResponse> handler)
        {
            IServiceProviderIsService? serviceProviderIsService = serviceProvider.GetService(typeof(IServiceProviderIsService)) as IServiceProviderIsService;

            if (serviceProviderIsService?.IsService(handlerType) == false)
            {
                throw new InvalidOperationException(
                    $"RequestHandlerWrapper: No handler registered for request type '{typeof(TRequest).FullName}' and response type '{typeof(TResponse).FullName}'.");
            }

            throw new InvalidOperationException(
                $"RequestHandlerWrapper: Handler resolution returned null for request type '{typeof(TRequest).FullName}' and response type '{typeof(TResponse).FullName}'.");
        }

        if (request is not TRequest typedRequest)
        {
            throw new InvalidOperationException(
                $"RequestHandlerWrapper: Expected request type '{typeof(TRequest).FullName}', but received '{request.GetType().FullName}'.");
        }

        // MS.DI resolves IEnumerable<T> as an empty collection when no services are registered.
        // Keep this fallback for compatibility with non-standard IServiceProvider implementations.
        IEnumerable<IPipelineBehavior<TRequest, TResponse>> behaviors = serviceProvider.GetService(typeof(IEnumerable<IPipelineBehavior<TRequest, TResponse>>)) as IEnumerable<IPipelineBehavior<TRequest, TResponse>>
            ?? [];

        if (behaviors is IPipelineBehavior<TRequest, TResponse>[] behaviorArray)
        {
            if (behaviorArray.Length == 0)
            {
                return handler.Handle(typedRequest, cancellationToken);
            }

            if (behaviorArray.Length == 1)
            {
                return behaviorArray[0].Handle(typedRequest, () => handler.Handle(typedRequest, cancellationToken), cancellationToken);
            }

            return InvokePipeline(behaviorArray, 0, typedRequest, handler, cancellationToken);
        }

        IList<IPipelineBehavior<TRequest, TResponse>> behaviorList = behaviors as IList<IPipelineBehavior<TRequest, TResponse>> ?? [.. behaviors];

        if (behaviorList.Count == 0)
        {
            return handler.Handle(typedRequest, cancellationToken);
        }

        if (behaviorList.Count == 1)
        {
            return behaviorList[0].Handle(typedRequest, () => handler.Handle(typedRequest, cancellationToken), cancellationToken);
        }

        return InvokePipeline(behaviorList, 0, typedRequest, handler, cancellationToken);
    }

    private static Task<TResponse> InvokePipeline(
        IList<IPipelineBehavior<TRequest, TResponse>> behaviors,
        int index,
        TRequest request,
        IRequestHandler<TRequest, TResponse> handler,
        CancellationToken cancellationToken)
    {
        if (index == behaviors.Count)
        {
            return handler.Handle(request, cancellationToken);
        }

        // Build the continuation lazily so deeper delegates are only allocated
        // when the current behavior actually invokes next().
        return behaviors[index].Handle(request,
            () => InvokePipeline(behaviors, index + 1, request, handler, cancellationToken),
            cancellationToken);
    }
}
