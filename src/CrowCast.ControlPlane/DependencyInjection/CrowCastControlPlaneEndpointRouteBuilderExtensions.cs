using CrowCast.ControlPlane.Grpc;
using CrowCast.ControlPlane.Realtime;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace CrowCast.ControlPlane.DependencyInjection;

public static class CrowCastControlPlaneEndpointRouteBuilderExtensions
{
    public static IEndpointRouteBuilder MapCrowCastControlPlane(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        endpoints.MapGrpcService<CrowCastObservationGrpcService>();
        endpoints.MapHub<CrowCastControlPlaneHub>("/_crowcast/control-plane/live");
        return endpoints;
    }
}
