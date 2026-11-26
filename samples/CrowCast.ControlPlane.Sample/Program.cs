using CrowCast.ControlPlane.DependencyInjection;
using CrowCast.ControlPlane.WebUI;
using Microsoft.AspNetCore.Server.Kestrel.Core;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseStaticWebAssets();
builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenLocalhost(5080, endpoint => endpoint.Protocols = HttpProtocols.Http1AndHttp2);
    options.ListenLocalhost(5081, endpoint => endpoint.Protocols = HttpProtocols.Http2);
});
builder.Services.AddCrowCastControlPlaneWebUI();

var app = builder.Build();
app.UseStaticFiles();
app.UseRouting();
app.MapRazorPages();
app.MapCrowCastControlPlane();
app.MapGet("/", () => Results.Redirect("/crowcast-control"));
await app.RunAsync();
