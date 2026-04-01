using Microsoft.Extensions.DependencyInjection;

namespace Mediora.Internal;

/// <summary>
/// Resolves and invokes the stream handler pipeline for a concrete stream request/response pair.
/// </summary>
internal sealed class StreamRequestHandlerWrapper<TRequest, TResponse> : IStreamRequestHandlerWrapper<TResponse>
    where TRequest : IStreamRequest<TResponse>
{
    public IAsyncEnumerable<TResponse> Handle(IStreamRequest<TResponse> request, IServiceProvider serviceProvider, CancellationToken cancellationToken)
    {
        Type handlerType = typeof(IStreamRequestHandler<TRequest, TResponse>);

        if (serviceProvider.GetService(handlerType) is not IStreamRequestHandler<TRequest, TResponse> handler)
        {
            IServiceProviderIsService? serviceProviderIsService = serviceProvider.GetService(typeof(IServiceProviderIsService)) as IServiceProviderIsService;

            if (serviceProviderIsService?.IsService(handlerType) == false)
            {
                throw new InvalidOperationException(
                    $"StreamRequestHandlerWrapper: No handler registered for stream request type '{typeof(TRequest).FullName}' and response type '{typeof(TResponse).FullName}'.");
            }

            throw new InvalidOperationException(
                $"StreamRequestHandlerWrapper: Handler resolution returned null for stream request type '{typeof(TRequest).FullName}' and response type '{typeof(TResponse).FullName}'.");
        }

        if (request is not TRequest typedRequest)
        {
            throw new InvalidOperationException(
                $"StreamRequestHandlerWrapper: Expected stream request type '{typeof(TRequest).FullName}', but received '{request.GetType().FullName}'.");
        }

        // MS.DI resolves IEnumerable<T> as an empty collection when no services are registered.
        // Keep this fallback for compatibility with non-standard IServiceProvider implementations.
        IEnumerable<IStreamPipelineBehavior<TRequest, TResponse>> behaviors = serviceProvider.GetService(typeof(IEnumerable<IStreamPipelineBehavior<TRequest, TResponse>>)) as IEnumerable<IStreamPipelineBehavior<TRequest, TResponse>>
            ?? [];

        if (behaviors is IStreamPipelineBehavior<TRequest, TResponse>[] behaviorArray)
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

        IList<IStreamPipelineBehavior<TRequest, TResponse>> behaviorList = behaviors as IList<IStreamPipelineBehavior<TRequest, TResponse>> ?? [.. behaviors];

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

    private static IAsyncEnumerable<TResponse> InvokePipeline(
        IList<IStreamPipelineBehavior<TRequest, TResponse>> behaviors,
        int index,
        TRequest request,
        IStreamRequestHandler<TRequest, TResponse> handler,
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
