namespace CrowCast.WebUI;

public sealed class CrowCastWebUIOptions
{
    public string RoutePrefix { get; set; } = "crowcast";

    public bool ExcludeFromOpenApi { get; set; } = true;
}
