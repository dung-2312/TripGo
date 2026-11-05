using Tripgo.Application.DTOs;
using Tripgo.Application.DTOs.Admin;

namespace Tripgo.Application.Interfaces;

/// <summary>All administrator operations. Every method authorizes the caller itself; the UI is not the boundary.</summary>
public interface IAdminService
{
    Task<AdminDashboardDto> GetDashboardAsync(CancellationToken ct = default);

    Task<List<AdminOperatorDto>> GetOperatorsAsync(string? search = null, CancellationToken ct = default);
    Task<AdminResult> SaveOperatorAsync(AdminOperatorInput input, CancellationToken ct = default);
    Task<AdminResult> SetOperatorActiveAsync(long operatorId, bool isActive, CancellationToken ct = default);
    Task<AdminResult> SetOperatorVerificationAsync(long operatorId, bool isVerified, string? evidence, CancellationToken ct = default);

    Task<List<LocationDto>> GetLocationsAsync(CancellationToken ct = default);
    Task<List<AdminRouteDto>> GetRoutesAsync(bool includeInactive = true, CancellationToken ct = default);
    Task<AdminRouteDto?> GetRouteAsync(long routeId, CancellationToken ct = default);
    Task<AdminResult> SaveRouteAsync(AdminRouteInput input, CancellationToken ct = default);
    Task<AdminResult> SetRouteActiveAsync(long routeId, bool isActive, CancellationToken ct = default);
    Task<AdminResult> SaveRouteStopsAsync(long routeId, List<AdminRouteStopDto> stops, CancellationToken ct = default);
    Task<List<AdminPointDto>> GetPointsAsync(long operatorId, CancellationToken ct = default);
    Task<AdminResult> AddPointAsync(AdminPointInput input, CancellationToken ct = default);

    Task<List<AdminBusDto>> GetBusesAsync(CancellationToken ct = default);
    Task<List<BusTypeDto>> GetBusTypesAsync(CancellationToken ct = default);
    Task<List<AdminSeatDto>> GetBusSeatsAsync(long busId, CancellationToken ct = default);
    Task<AdminResult> CreateBusAsync(CreateBusDto input, CancellationToken ct = default);
    Task<AdminResult> ReplaceBusLayoutAsync(long busId, string seatLayoutDefinition, CancellationToken ct = default);
    Task<AdminResult> SetBusActiveAsync(long busId, bool isActive, CancellationToken ct = default);

    Task<List<AdminDriverDto>> GetDriversAsync(CancellationToken ct = default);
    Task<AdminResult> SaveDriverAsync(AdminDriverInput input, CancellationToken ct = default);
    Task<AdminResult> SetDriverActiveAsync(long driverId, bool isActive, CancellationToken ct = default);

    Task<PagedResult<AdminTripDto>> GetTripsAsync(AdminTripFilter filter, CancellationToken ct = default);
    Task<AdminResult> SaveTripAsync(AdminTripInput input, CancellationToken ct = default);
    Task<AdminResult> SetTripStatusAsync(long tripId, string status, CancellationToken ct = default);
    Task<AdminResult> CancelTripAsync(long tripId, string reason, CancellationToken ct = default);

    Task<PagedResult<AdminBookingRowDto>> GetBookingsAsync(AdminBookingFilter filter, CancellationToken ct = default);
    Task<AdminBookingDetailDto?> GetBookingAsync(long bookingId, bool revealContact = false, CancellationToken ct = default);
    Task<AdminResult> CancelBookingAsync(long bookingId, string reason, CancellationToken ct = default);

    Task<PagedResult<AdminAuditRowDto>> GetAuditLogAsync(int page, CancellationToken ct = default);
    Task<PagedResult<AdminEmailRowDto>> GetEmailOutboxAsync(string? status, int page, CancellationToken ct = default);
    Task<AdminResult> RetryEmailAsync(long outboxId, CancellationToken ct = default);
    Task<PagedResult<AdminPaymentEventDto>> GetPaymentEventsAsync(bool needsReviewOnly, int page, CancellationToken ct = default);
}
