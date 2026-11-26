using CrowCast.WebUI.Realtime;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CrowCast.WebUI;

public static class CrowCastWebUIEndpointRouteBuilderExtensions
{
    public static IEndpointRouteBuilder MapCrowCastWebUI(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        var options = endpoints.ServiceProvider.GetRequiredService<IOptions<CrowCastWebUIOptions>>().Value;
        var liveHub = endpoints.MapHub<CrowCastWebUIHub>($"/{ServiceCollectionExtensions.Normalize(options.RoutePrefix)}/live");
        if (options.ExcludeFromOpenApi) liveHub.ExcludeFromDescription();
        return endpoints;
    }
}
