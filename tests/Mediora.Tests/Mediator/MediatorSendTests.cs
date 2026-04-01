using Microsoft.Extensions.DependencyInjection;

namespace Mediora.Tests;

public sealed class MediatorSendTests
{
    [Fact]
    public async Task Send_ResolvesAndInvokesCorrectHandler()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IRequestHandler<PingRequest, string>, PingHandler>();
        services.AddSingleton<IMediator, Mediator>();

        await using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        var response = await mediator.Send(new PingRequest("ping"));

        Assert.Equal("PING", response);
    }

    [Fact]
    public async Task Send_ThrowsArgumentNullException_WhenRequestIsNull()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IMediator, Mediator>();

        await using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        var exception = await Assert.ThrowsAsync<ArgumentNullException>(() => mediator.Send<string>(null!));

        Assert.Equal("request", exception.ParamName);
    }

    [Fact]
    public async Task Send_ThrowsInvalidOperationException_WhenNoHandlerIsRegistered()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IMediator, Mediator>();

        await using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        await Assert.ThrowsAsync<InvalidOperationException>(() => mediator.Send(new UnhandledRequest()));
    }

    [Fact]
    public async Task Send_PassesCancellationTokenToHandler()
    {
        var tokenStore = new TokenStore();
        var services = new ServiceCollection();
        services.AddSingleton(tokenStore);
        services.AddSingleton<IRequestHandler<TokenRequest, string>, TokenHandler>();
        services.AddSingleton<IMediator, Mediator>();

        await using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();
        using var cts = new CancellationTokenSource();

        await mediator.Send(new TokenRequest(), cts.Token);

        Assert.Equal(cts.Token, tokenStore.LastToken);
    }

    [Fact]
    public async Task Send_DispatchesDifferentRequestTypesToMatchingHandlers()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IRequestHandler<SumRequest, int>, SumHandler>();
        services.AddSingleton<IRequestHandler<ConcatRequest, string>, ConcatHandler>();
        services.AddSingleton<IMediator, Mediator>();

        await using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        var sum = await mediator.Send(new SumRequest(2, 3));
        var concat = await mediator.Send(new ConcatRequest("me", "diora"));

        Assert.Equal(5, sum);
        Assert.Equal("mediora", concat);
    }

    [Fact]
    public async Task Send_SupportsNonGenericIRequestAndIRequestHandler()
    {
        var services = new ServiceCollection();
        services.AddMediora(options => options.RegisterServicesFromAssembly(typeof(VoidRequestHandler).Assembly));

        await using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        var response = await mediator.Send(new VoidRequest());

        Assert.Equal(Unit.Value, response);
    }

    [Fact]
    public async Task Send_PropagatesHandlerException()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IRequestHandler<ThrowingRequest, string>, ThrowingHandler>();
        services.AddSingleton<IMediator, Mediator>();

        await using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => mediator.Send(new ThrowingRequest()));

        Assert.Equal("handler-failure", exception.Message);
    }

    [Fact]
    public async Task Send_ThrowsOperationCanceledException_WhenHandlerObservesCancellation()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IRequestHandler<CancellableRequest, string>, CancellableHandler>();
        services.AddSingleton<IMediator, Mediator>();

        await using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => mediator.Send(new CancellableRequest(), cts.Token));
    }

    private sealed record PingRequest(string Value) : IRequest<string>;

    private sealed class PingHandler : IRequestHandler<PingRequest, string>
    {
        public Task<string> Handle(PingRequest request, CancellationToken cancellationToken)
        {
            return Task.FromResult(request.Value.ToUpperInvariant());
        }
    }

    private sealed record UnhandledRequest : IRequest<Unit>;

    private sealed record TokenRequest : IRequest<string>;

    private sealed class TokenStore
    {
        public CancellationToken LastToken { get; set; }
    }

    private sealed class TokenHandler : IRequestHandler<TokenRequest, string>
    {
        private readonly TokenStore _tokenStore;

        public TokenHandler(TokenStore tokenStore)
        {
            _tokenStore = tokenStore;
        }

        public Task<string> Handle(TokenRequest request, CancellationToken cancellationToken)
        {
            _tokenStore.LastToken = cancellationToken;
            return Task.FromResult("ok");
        }
    }

    private sealed record SumRequest(int Left, int Right) : IRequest<int>;

    private sealed class SumHandler : IRequestHandler<SumRequest, int>
    {
        public Task<int> Handle(SumRequest request, CancellationToken cancellationToken)
        {
            return Task.FromResult(request.Left + request.Right);
        }
    }

    private sealed record ConcatRequest(string Left, string Right) : IRequest<string>;

    private sealed class ConcatHandler : IRequestHandler<ConcatRequest, string>
    {
        public Task<string> Handle(ConcatRequest request, CancellationToken cancellationToken)
        {
            return Task.FromResult(request.Left + request.Right);
        }
    }

    private sealed record VoidRequest : IRequest;

    private sealed class VoidRequestHandler : IRequestHandler<VoidRequest>
    {
        public Task Handle(VoidRequest request, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }

    private sealed record ThrowingRequest : IRequest<string>;

    private sealed class ThrowingHandler : IRequestHandler<ThrowingRequest, string>
    {
        public Task<string> Handle(ThrowingRequest request, CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("handler-failure");
        }
    }

    private sealed record CancellableRequest : IRequest<string>;

    private sealed class CancellableHandler : IRequestHandler<CancellableRequest, string>
    {
        public Task<string> Handle(CancellableRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult("ok");
        }
    }
}
