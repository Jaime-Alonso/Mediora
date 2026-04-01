namespace Mediora;

/// <summary>
/// Represents a request that produces a stream of responses.
/// </summary>
/// <typeparam name="TResponse">The response type produced by the stream.</typeparam>
public interface IStreamRequest<out TResponse>
{
}
