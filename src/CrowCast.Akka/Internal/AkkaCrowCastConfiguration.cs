using System.Text;
using Akka.Configuration;
using CrowCast.Akka.Configuration;
using CrowCast.Backends;

namespace CrowCast.Akka.Internal;

internal static class AkkaCrowCastConfiguration
{
    public static string GetActorSystemName(string clusterName)
    {
        var normalized = new StringBuilder(clusterName.Length);
        foreach (var character in clusterName.ToLowerInvariant())
        {
            normalized.Append(char.IsLetterOrDigit(character) || character is '-' or '_'
                ? character
                : '-');
        }

        var value = normalized.ToString().Trim('-');
        return string.IsNullOrEmpty(value) ? "crowcast" : $"crowcast-{value}";
    }

    public static Config Create(CrowCastBackendSettings settings, AkkaCrowCastOptions options)
    {
        var stableAfter = settings.DeadAfter - settings.SuspectAfter;
        var publicHostName = string.IsNullOrWhiteSpace(options.PublicHostName)
            ? string.Empty
            : $"public-hostname = \"{Escape(options.PublicHostName)}\"";
        var publicPort = options.PublicPort.HasValue
            ? $"public-port = {options.PublicPort.Value}"
            : string.Empty;

        var hocon = $$"""
            akka {
              loglevel = WARNING
              stdout-loglevel = WARNING
              actor {
                provider = cluster
                serializers {
                  crowcast = "CrowCast.Akka.Serialization.CrowCastAkkaSerializer, CrowCast.Akka"
                }
                serialization-bindings {
                  "CrowCast.Akka.Transport.ICrowCastWireMessage, CrowCast.Akka" = crowcast
                }
              }
              extensions = ["Akka.Cluster.Tools.PublishSubscribe.DistributedPubSubExtensionProvider, Akka.Cluster.Tools"]
              remote.dot-netty.tcp {
                hostname = "{{Escape(options.HostName)}}"
                port = {{options.Port}}
                {{publicHostName}}
                {{publicPort}}
              }
              cluster {
                gossip-interval = {{FormatDuration(settings.ExchangeInterval)}}
                downing-provider-class = "Akka.Cluster.SBR.SplitBrainResolverProvider, Akka.Cluster"
                failure-detector {
                  heartbeat-interval = {{FormatDuration(settings.ExchangeInterval)}}
                  acceptable-heartbeat-pause = {{FormatDuration(settings.SuspectAfter)}}
                }
                split-brain-resolver {
                  active-strategy = keep-majority
                  stable-after = {{FormatDuration(stableAfter)}}
                  down-all-when-unstable = off
                }
              }
              cluster.pub-sub {
                gossip-interval = {{FormatDuration(settings.ExchangeInterval)}}
                send-to-dead-letters-when-no-subscribers = off
              }
              coordinated-shutdown {
                terminate-actor-system = on
                run-by-actor-system-terminate = off
                exit-clr = off
              }
            }
            """;

        var configuration = ConfigurationFactory.ParseString(hocon);
        return string.IsNullOrWhiteSpace(options.AdditionalConfiguration)
            ? configuration
            : ConfigurationFactory.ParseString(options.AdditionalConfiguration).WithFallback(configuration);
    }

    private static string FormatDuration(TimeSpan value) =>
        $"{Math.Max(1, Math.Ceiling(value.TotalMilliseconds)):0}ms";

    private static string Escape(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);
}
