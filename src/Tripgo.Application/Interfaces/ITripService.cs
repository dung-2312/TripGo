using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Tripgo.Application.DTOs;

namespace Tripgo.Application.Interfaces;

public interface ITripService
{
    Task<List<LocationDto>> GetLocationsAsync(CancellationToken cancellationToken = default);
    Task<long> CreateOperatorAsync(CreateOperatorDto dto, CancellationToken ct = default);
    Task<long> CreateBusWithLayoutAsync(CreateBusDto dto, CancellationToken ct = default);
    Task<List<BusTypeDto>> GetBusTypesAsync(CancellationToken ct = default);
    Task<List<TripSearchResultDto>> SearchTripsAsync(TripSearchRequestDto request, CancellationToken cancellationToken = default);
    Task<TripDetailDto?> GetTripDetailsAsync(long tripId, CancellationToken cancellationToken = default);
    Task<List<OperatorDto>> GetOperatorsAsync(CancellationToken ct = default);
    Task<List<BusSelectDto>> GetBusesByOperatorAsync(long operatorId, CancellationToken ct = default);
    Task<List<RouteDto>> GetRoutesAsync(CancellationToken ct = default);
    Task<long> CreateRouteAsync(CreateRouteDto dto, CancellationToken ct = default);
    Task<long> CreateTripAsync(CreateTripDto dto, CancellationToken ct = default);
    Task<bool> DeleteTripAsync(long tripId, CancellationToken ct = default);
    Task<List<TripSearchResultDto>> GetAllTripsAdminAsync(CancellationToken ct = default);
    Task<bool> BookSeatsAsync(long tripId, List<string> seatCodes, CustomerBookingInfoDto customerInfo, CancellationToken ct = default);
    Task<List<CustomerBookingInfoDto>> GetBookingsByPhoneAsync(string phone, CancellationToken ct = default);
    Task<CustomerBookingInfoDto?> GetBookingByCodeAsync(string bookingCode, CancellationToken ct = default);
    Task<List<CustomerBookingInfoDto>> GetBookingsForCurrentUserAsync(CancellationToken ct = default);
    Task<bool> SubmitSupportCallbackAsync(string name, string phone, CancellationToken ct = default);
}
