using CrowCast.Observability.Models;

namespace CrowCast.Observability;

public interface ICrowCastObservationSource
{
    CrowCastObservationSnapshot GetSnapshot();

    IReadOnlyList<CrowCastObservationEvent> GetEvents(long afterSequence = 0, int take = 200);

    IReadOnlyList<CrowCastObservationSample> GetSamples(int take = 200);

    event Action? Changed;
}
