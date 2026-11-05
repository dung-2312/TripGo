using Tripgo.Application.DTOs;
using Tripgo.Application.DTOs.Admin;
using Tripgo.Domain.Entities;

namespace Tripgo.Application.Interfaces.Persistence;

public interface IAdminRepository
{
    Task<AdminActor?> GetActiveActorAsync(long userId, CancellationToken ct = default);
    Task AddAuditAsync(AdminAuditLog entry, CancellationToken ct = default);

    Task<AdminDashboardDto> GetDashboardAsync(DateTimeOffset now, CancellationToken ct = default);

    Task<List<AdminOperatorDto>> GetOperatorsAsync(string? search, CancellationToken ct = default);
    Task<bool> OperatorCodeOrTaxCodeTakenAsync(string code, string? taxCode, long exceptId, CancellationToken ct = default);
    Task<bool> UpdateOperatorAsync(AdminOperatorInput input, CancellationToken ct = default);
    Task<bool> SetOperatorActiveAsync(long operatorId, bool isActive, CancellationToken ct = default);
    Task<bool> SetOperatorVerificationAsync(long operatorId, bool isVerified, string? evidence, long actorUserId, DateTimeOffset now, CancellationToken ct = default);

    Task<List<AdminRouteDto>> GetRoutesAsync(bool includeInactive, CancellationToken ct = default);
    Task<AdminRouteDto?> GetRouteAsync(long routeId, CancellationToken ct = default);
    Task<bool> ActiveRouteExistsAsync(long operatorId, long originId, long destinationId, long exceptId, CancellationToken ct = default);
    Task<bool> RouteCodeTakenAsync(string code, long exceptId, CancellationToken ct = default);
    Task<bool> UpdateRouteAsync(AdminRouteInput input, CancellationToken ct = default);
    Task<bool> SetRouteSourceReferenceAsync(long routeId, string? sourceReference, CancellationToken ct = default);
    Task<bool> SetRouteActiveAsync(long routeId, bool isActive, CancellationToken ct = default);
    Task<int> CountUpcomingTripsForRouteAsync(long routeId, DateTimeOffset now, CancellationToken ct = default);
    Task ReplaceRouteStopsAsync(long routeId, IReadOnlyList<RouteStop> stops, CancellationToken ct = default);
    Task<List<AdminPointDto>> GetPointsAsync(long operatorId, CancellationToken ct = default);
    Task<List<long>> GetActivePointIdsAsync(IReadOnlyCollection<long> pointIds, CancellationToken ct = default);
    Task<long> AddPointAsync(PickupDropoffPoint point, CancellationToken ct = default);

    Task<List<AdminBusDto>> GetBusesAsync(DateTimeOffset now, CancellationToken ct = default);
    Task<List<AdminSeatDto>> GetBusSeatsAsync(long busId, CancellationToken ct = default);
    Task<bool> LicensePlateTakenAsync(string plate, CancellationToken ct = default);
    Task<int> CountUpcomingTripsForBusAsync(long busId, DateTimeOffset now, CancellationToken ct = default);
    Task<bool> ReplaceBusLayoutAsync(long busId, List<SeatLayoutItem> items, CancellationToken ct = default);
    Task<bool> SetBusActiveAsync(long busId, bool isActive, CancellationToken ct = default);

    Task<List<AdminDriverDto>> GetDriversAsync(CancellationToken ct = default);
    Task<Driver?> GetDriverAsync(long driverId, CancellationToken ct = default);
    Task<bool> DriverLicenseTakenAsync(string licenseNumber, long exceptId, CancellationToken ct = default);
    Task<long> SaveDriverAsync(AdminDriverInput input, CancellationToken ct = default);
    Task<bool> SetDriverActiveAsync(long driverId, bool isActive, CancellationToken ct = default);
    Task<List<Driver>> GetDriversByIdsAsync(IReadOnlyCollection<long> driverIds, CancellationToken ct = default);
    Task<bool> DriverHasConflictAsync(long driverId, DateTimeOffset departure, DateTimeOffset arrival, long exceptTripId, CancellationToken ct = default);

    Task<PagedResult<AdminTripDto>> GetTripsAsync(AdminTripFilter filter, DateTimeOffset now, CancellationToken ct = default);
    Task<Trip?> GetTripAsync(long tripId, CancellationToken ct = default);
    Task<Route?> GetActiveRouteAsync(long routeId, CancellationToken ct = default);
    Task<Bus?> GetActiveBusWithLayoutAsync(long busId, CancellationToken ct = default);
    Task<bool> BusHasConflictAsync(long busId, DateTimeOffset departure, DateTimeOffset arrival, long exceptTripId, CancellationToken ct = default);
    Task<int> CountActiveBookingsAsync(long tripId, CancellationToken ct = default);
    Task FinalizeNewTripAsync(long tripId, bool openAsSandbox, CancellationToken ct = default);
    Task<bool> UpdateTripScheduleAsync(AdminTripInput input, CancellationToken ct = default);
    Task ReplaceTripDriversAsync(long tripId, IReadOnlyCollection<long> driverIds, CancellationToken ct = default);
    Task<bool> SetTripStatusAsync(long tripId, Tripgo.Domain.Enums.TripStatus status, CancellationToken ct = default);
    Task<TripCancellationSummary> CancelTripAsync(long tripId, string reason, DateTimeOffset now, CancellationToken ct = default);

    Task<PagedResult<AdminBookingRowDto>> GetBookingsAsync(AdminBookingFilter filter, CancellationToken ct = default);
    Task<AdminBookingDetailDto?> GetBookingAsync(long bookingId, bool revealContact, CancellationToken ct = default);
    Task<AdminResult> CancelUnpaidBookingAsync(long bookingId, string reason, DateTimeOffset now, CancellationToken ct = default);

    Task<PagedResult<AdminAuditRowDto>> GetAuditLogAsync(int page, int pageSize, CancellationToken ct = default);
    Task<PagedResult<AdminEmailRowDto>> GetEmailOutboxAsync(string? status, int page, int pageSize, CancellationToken ct = default);
    Task<bool> RetryFailedEmailAsync(long outboxId, DateTimeOffset now, CancellationToken ct = default);
    Task<PagedResult<AdminPaymentEventDto>> GetPaymentEventsAsync(bool needsReviewOnly, int page, int pageSize, CancellationToken ct = default);
}
