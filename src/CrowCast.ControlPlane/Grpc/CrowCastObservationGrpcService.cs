using CrowCast.ControlPlane.Protocol.V1;
using CrowCast.ControlPlane.Storage;
using CrowCast.Observability.Models;
using Grpc.Core;

namespace CrowCast.ControlPlane.Grpc;

public sealed class CrowCastObservationGrpcService(
    ICrowCastControlPlaneStore store,
    TimeProvider timeProvider) : CrowCastObservationService.CrowCastObservationServiceBase
{
    public override async Task Observe(
        IAsyncStreamReader<ObservationFrame> requestStream,
        IServerStreamWriter<ControlPlaneFrame> responseStream,
        ServerCallContext context)
    {
        CrowCastClusterKey? cluster = null;
        CrowCastObserverIdentity? observer = null;
        long highestFrame = 0;
        try
        {
            while (await requestStream.MoveNext(context.CancellationToken).ConfigureAwait(false))
            {
                var frame = requestStream.Current;
                if (frame.FrameSequence <= highestFrame)
                {
                    await responseStream.WriteAsync(Resync("Frame sequence is not monotonic."), context.CancellationToken).ConfigureAwait(false);
                    continue;
                }

                highestFrame = frame.FrameSequence;
                if (cluster is null)
                {
                    if (frame.PayloadCase != ObservationFrame.PayloadOneofCase.Hello || frame.Hello.SchemaVersion != 1 ||
                        frame.Hello.Cluster is null || frame.Hello.Observer is null)
                    {
                        await responseStream.WriteAsync(Reject("The first frame must be a supported hello."), context.CancellationToken).ConfigureAwait(false);
                        return;
                    }

                    cluster = CrowCastProtocolMapper.Map(frame.Hello.Cluster);
                    observer = CrowCastProtocolMapper.Map(frame.Hello.Observer);
                    if (!Valid(cluster, observer))
                    {
                        await responseStream.WriteAsync(Reject("Cluster and observer identities are required."), context.CancellationToken).ConfigureAwait(false);
                        return;
                    }

                    store.Connect(cluster, observer, timeProvider.GetUtcNow());
                    await responseStream.WriteAsync(new ControlPlaneFrame
                    {
                        Accepted = new Accepted { SchemaVersion = 1, ServerUnixMs = timeProvider.GetUtcNow().ToUnixTimeMilliseconds() }
                    }, context.CancellationToken).ConfigureAwait(false);
                }
                else
                {
                    Process(frame, cluster, observer!, timeProvider.GetUtcNow());
                }

                await responseStream.WriteAsync(new ControlPlaneFrame
                {
                    Acknowledgement = new Acknowledgement { FrameSequence = highestFrame }
                }, context.CancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            if (cluster is not null && observer is not null)
            {
                store.Disconnect(cluster, observer, timeProvider.GetUtcNow());
            }
        }
    }

    private void Process(ObservationFrame frame, CrowCastClusterKey cluster, CrowCastObserverIdentity observer, DateTimeOffset now)
    {
        switch (frame.PayloadCase)
        {
            case ObservationFrame.PayloadOneofCase.Snapshot:
                var snapshot = CrowCastProtocolMapper.Map(frame.Snapshot);
                if (snapshot.Cluster == cluster && snapshot.Observer == observer) store.Upsert(snapshot, now);
                break;
            case ObservationFrame.PayloadOneofCase.Events:
                store.Append(cluster, observer, frame.Events.Events.Select(CrowCastProtocolMapper.Map).ToArray(), now);
                break;
            case ObservationFrame.PayloadOneofCase.Heartbeat:
                store.Touch(cluster, observer, now);
                break;
            case ObservationFrame.PayloadOneofCase.Goodbye:
                store.Disconnect(cluster, observer, now);
                break;
        }
    }

    private static bool Valid(CrowCastClusterKey cluster, CrowCastObserverIdentity observer) =>
        !string.IsNullOrWhiteSpace(cluster.Environment) && !string.IsNullOrWhiteSpace(cluster.Service) &&
        !string.IsNullOrWhiteSpace(cluster.Cluster) && !string.IsNullOrWhiteSpace(observer.NodeId) &&
        !string.IsNullOrWhiteSpace(observer.Generation);

    private static ControlPlaneFrame Reject(string reason) => new() { Rejected = new Rejected { Reason = reason } };

    private static ControlPlaneFrame Resync(string reason) => new() { ResyncRequired = new ResyncRequired { Reason = reason } };
}
