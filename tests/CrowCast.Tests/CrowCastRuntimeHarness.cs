using CrowCast.Backends;
using CrowCast.DependencyInjection;
using CrowCast.Diagnostics;
using CrowCast.Events;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace CrowCast.Tests;

internal sealed class CrowCastRuntimeHarness : IAsyncDisposable
{
    private readonly ServiceProvider _provider;
    private readonly IHostedService _hostedService;

    private CrowCastRuntimeHarness(
        ServiceProvider provider,
        IHostedService hostedService,
        FakeCrowCastBackend backend,
        RecordingEventHandler events,
        ICrowCast crowCast,
        ICrowCastDiagnostics diagnostics)
    {
        _provider = provider;
        _hostedService = hostedService;
        Backend = backend;
        Events = events;
        CrowCast = crowCast;
        Diagnostics = diagnostics;
    }

    public FakeCrowCastBackend Backend { get; }

    public RecordingEventHandler Events { get; }

    public ICrowCast CrowCast { get; }

    public ICrowCastDiagnostics Diagnostics { get; }

    public static async Task<CrowCastRuntimeHarness> StartAsync(
        Action<CrowCast.Configuration.CrowCastOptions>? configure = null)
    {
        var services = new ServiceCollection();
        var backend = new FakeCrowCastBackend();
        var events = new RecordingEventHandler();

        services.AddLogging();
        services.AddSingleton<ICrowCastBackend>(backend);
        services.AddSingleton<ICrowCastEventHandler>(events);
        services.AddCrowCast(configure);

        var provider = services.BuildServiceProvider();
        var hostedService = provider.GetServices<IHostedService>().Single();
        var crowCast = provider.GetRequiredService<ICrowCast>();
        var diagnostics = provider.GetRequiredService<ICrowCastDiagnostics>();
        await hostedService.StartAsync(CancellationToken.None);

        return new CrowCastRuntimeHarness(provider, hostedService, backend, events, crowCast, diagnostics);
    }

    public async ValueTask DisposeAsync()
    {
        await _hostedService.StopAsync(CancellationToken.None);
        await _provider.DisposeAsync();
    }
}
