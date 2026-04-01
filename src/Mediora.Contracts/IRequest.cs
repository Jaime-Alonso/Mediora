namespace Mediora;

/// <summary>
/// Represents a request that produces a single response.
/// </summary>
/// <typeparam name="TResponse">The response type produced by the request.</typeparam>
public interface IRequest<out TResponse>
{
}


/// <summary>
/// Represents a request that does not return meaningful data.
/// </summary>
public interface IRequest : IRequest<Unit>
{
}
