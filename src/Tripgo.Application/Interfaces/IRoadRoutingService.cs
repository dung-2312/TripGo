using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Tripgo.Application.Interfaces;

public class RoadRoutingResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public List<double[]> Geometry { get; set; } = new(); // Array of [latitude, longitude]
    public double DistanceMeters { get; set; }
    public double DurationSeconds { get; set; }
    public string ProviderSource { get; set; } = "OSRM Road Network";
}

public interface IRoadRoutingService
{
    Task<RoadRoutingResult> GetDrivingRouteAsync(
        double originLat,
        double originLng,
        double destLat,
        double destLng,
        CancellationToken ct = default);
}

