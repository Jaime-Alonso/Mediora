namespace Mediora;

/// <summary>
/// Combines request sending, stream request sending, and notification publishing capabilities.
/// </summary>
public interface IMediator : ISender, IPublisher
{
}
