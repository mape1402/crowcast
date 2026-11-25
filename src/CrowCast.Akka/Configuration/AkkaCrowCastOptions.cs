namespace CrowCast.Akka.Configuration;

public sealed class AkkaCrowCastOptions
{
    public string HostName { get; set; } = "127.0.0.1";

    public int Port { get; set; }

    public string? PublicHostName { get; set; }

    public int? PublicPort { get; set; }

    public TimeSpan StartupTimeout { get; set; } = TimeSpan.FromSeconds(15);

    public TimeSpan ShutdownTimeout { get; set; } = TimeSpan.FromSeconds(15);

    public string? AdditionalConfiguration { get; set; }
}
