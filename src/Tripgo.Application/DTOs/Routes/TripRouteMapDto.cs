using System;
using System.Collections.Generic;

namespace Tripgo.Application.DTOs;

public class TripRouteMapDto
{
    public long TripId { get; set; }
    public string TripCode { get; set; } = string.Empty;
    public string RouteName { get; set; } = string.Empty;
    public string OperatorName { get; set; } = string.Empty;
    public string BusLicensePlate { get; set; } = string.Empty;
    public string BusTypeName { get; set; } = string.Empty;

    // Selected endpoints
    public RouteMapPointDto SelectedPickupPoint { get; set; } = new();
    public RouteMapPointDto SelectedDropoffPoint { get; set; } = new();

    // Road routing geometry & metrics
    public List<double[]> PolylineCoordinates { get; set; } = new();
    public decimal DistanceKm { get; set; }
    public string RoutingSource { get; set; } = "OSRM Road Network (OpenStreetMap)";
    public bool IsRouteFound { get; set; } = true;
    public string? ErrorMessage { get; set; }

    // Journey duration breakdown
    public string OperatorScheduledDurationText { get; set; } = string.Empty;
    public string RoadEstimatedDurationText { get; set; } = string.Empty;
    public int RestStopDurationMinutes { get; set; }
    public string DurationSummary { get; set; } = string.Empty;
    public string DurationNotice { get; set; } = "Thời gian di chuyển thực tế phụ thuộc vào điều kiện giao thông và dừng đỗ.";

    // Verified rest stops along this segment
    public List<RouteMapPointDto> RestStops { get; set; } = new();
    public bool HasVerifiedRestStops => RestStops.Count > 0;

    // Legacy compatibility for any existing view binding
    public List<RouteMapPointDto> Points { get; set; } = new();
    public string DurationText => DurationSummary;
    public DateTimeOffset DepartureTime { get; set; }
    public DateTimeOffset ArrivalTime { get; set; }
}

public class RouteMapPointDto
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public string PointType { get; set; } = "Pickup"; // "Pickup", "Dropoff", "RestStop"
    public string ScheduledTime { get; set; } = string.Empty;
    public int? StayMinutes { get; set; }
    public string Description { get; set; } = string.Empty;
    public string Services { get; set; } = string.Empty;
    public string VerificationStatus { get; set; } = "Chưa xác minh";
    public string? DataSource { get; set; }
}
