using CrowCast.ControlPlane.DependencyInjection;
using CrowCast.ControlPlane.Storage;
using CrowCast.ControlPlane.WebUI;
using CrowCast.ControlPlane.WebUI.Navigation;
using CrowCast.WebUI;
using CrowCast.WebUI.Navigation;
using CrowCast.WebUI.Shell;
using CrowCast.WebUI.Shell.Navigation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Zoorce.Web.UI.Abstractions.Navigation;
using Zoorce.Web.UI.Layout;
using Zoorce.Web.UI.Theming;

namespace CrowCast.Tests.WebUI;

public sealed class WebUiRegistrationTests
{
    [Fact]
    public void Registrations_are_composable_and_apply_routes()
    {
        Assert.Throws<ArgumentNullException>(() => CrowCast.WebUI.ServiceCollectionExtensions.AddCrowCastWebUI(null!));
        Assert.Throws<ArgumentNullException>(() => CrowCast.ControlPlane.WebUI.ServiceCollectionExtensions.AddCrowCastControlPlaneWebUI(null!));
        Assert.Throws<ArgumentNullException>(() => CrowCast.WebUI.Shell.ServiceCollectionExtensions.AddCrowCastWebUIShell(null!));
        Assert.Throws<ArgumentNullException>(() => CrowCastControlPlaneServiceCollectionExtensions.AddCrowCastControlPlane(null!));

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCrowCastWebUI(options => options.RoutePrefix = "local");
        services.AddCrowCastControlPlaneWebUI(options => options.RoutePrefix = "fleet");
        using var provider = services.BuildServiceProvider();
        var routes = provider.GetRequiredService<IOptions<RazorPagesOptions>>().Value;
        Assert.NotEmpty(routes.Conventions);
        Assert.NotNull(provider.GetRequiredService<ICrowCastControlPlaneStore>());
        Assert.Equal("local", provider.GetRequiredService<IOptions<CrowCastWebUIOptions>>().Value.RoutePrefix);
        Assert.Equal("fleet", provider.GetRequiredService<IOptions<CrowCastControlPlaneWebUIOptions>>().Value.RoutePrefix);
        var shell = provider.GetRequiredService<IOptions<ZoorceShellOptions>>().Value;
        Assert.Equal(ZoorceThemePreset.Teal, shell.Themes.ActivePreset);
        Assert.Equal(ZoorceThemeMode.Light, shell.DefaultTheme);
        Assert.Null(shell.ThemeSelector);
        Assert.Equal("crowcast", CrowCast.WebUI.ServiceCollectionExtensions.Normalize(" "));
        Assert.Equal("crowcast-control", CrowCast.ControlPlane.WebUI.ServiceCollectionExtensions.Normalize(" "));

        var defaultUi = new ServiceCollection();
        defaultUi.AddLogging();
        defaultUi.AddCrowCastWebUI();
        using var defaultProvider = defaultUi.BuildServiceProvider();
        _ = defaultProvider.GetRequiredService<IOptions<RazorPagesOptions>>().Value;

        var configuredControlPlane = new ServiceCollection();
        configuredControlPlane.AddLogging();
        configuredControlPlane.AddCrowCastControlPlane(options => options.MaximumEventsPerCluster = 7);
        using var configuredProvider = configuredControlPlane.BuildServiceProvider();
        Assert.Equal(7, configuredProvider.GetRequiredService<IOptions<CrowCast.ControlPlane.Configuration.CrowCastControlPlaneOptions>>().Value.MaximumEventsPerCluster);
    }

    [Fact]
    public async Task Embedded_endpoint_mapping_adds_the_configured_live_hub()
    {
        Assert.Throws<ArgumentNullException>(() => CrowCastWebUIEndpointRouteBuilderExtensions.MapCrowCastWebUI(null!));
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddCrowCastWebUI(options => options.RoutePrefix = "/ops/local/");
        await using var app = builder.Build();
        app.MapCrowCastWebUI();

        var routes = (IEndpointRouteBuilder)app;
        Assert.Contains(routes.DataSources.SelectMany(source => source.Endpoints), endpoint =>
            endpoint.DisplayName?.Contains("/ops/local/live", StringComparison.Ordinal) == true);
    }

    [Fact]
    public void Navigation_contributors_and_registry_order_items()
    {
        var embedded = new EmbeddedNavigationContributor();
        var control = new ControlPlaneNavigationContributor();
        Assert.Equal(5, embedded.GetItems().Count);
        Assert.Equal(2, control.GetItems().Count);
        var registry = new CrowCastNavigationRegistry([control, embedded]);
        Assert.Equal("Overview", registry.GetItems()[0].Label);
        Assert.Equal("Clusters", registry.GetItems()[^1].Label);
    }

    [Fact]
    public void Zoorce_navigation_provider_handles_missing_and_present_http_context()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRouting();
        services.AddRazorPages();
        using var provider = services.BuildServiceProvider();
        var accessor = new HttpContextAccessor();
        var registry = new CrowCastNavigationRegistry([new EmbeddedNavigationContributor(), new ControlPlaneNavigationContributor()]);
        var navigation = new ZoorceCrowCastNavigationProvider(registry, provider.GetRequiredService<LinkGenerator>(), accessor);
        Assert.Empty(navigation.GetItems());
        accessor.HttpContext = new DefaultHttpContext { RequestServices = provider };
        var items = navigation.GetItems();
        Assert.Equal(2, items.Count);
        Assert.All(items, item => Assert.True(item.HasChildren));
    }
}
