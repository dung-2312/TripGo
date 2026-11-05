using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Tripgo.Domain.Entities;

namespace Tripgo.Application.Interfaces.Persistence;

public interface ITripRepository
{
    // Locations
    Task<List<Location>> GetActiveLocationsAsync(CancellationToken ct = default);

    // Operators
    Task<List<Operator>> GetActiveOperatorsAsync(CancellationToken ct = default);
    Task<bool> OperatorExistsAndActiveAsync(long id, CancellationToken ct = default);
    Task AddOperatorAsync(Operator operatorEntity, CancellationToken ct = default);

    // Bus Types
    Task<List<BusType>> GetActiveBusTypesAsync(CancellationToken ct = default);
    Task<BusType?> GetActiveBusTypeByIdAsync(long id, CancellationToken ct = default);

    // Buses
    Task<List<Bus>> GetActiveBusesByOperatorAsync(long operatorId, CancellationToken ct = default);
    Task<bool> BusExistsAndActiveAsync(long id, CancellationToken ct = default);
    Task<long?> GetBusOperatorIdAsync(long busId, CancellationToken ct = default);
    Task AddBusAsync(Bus bus, CancellationToken ct = default);

    // Routes
    Task<List<Route>> GetActiveRoutesAsync(CancellationToken ct = default);
    Task<long?> GetRouteOperatorIdAsync(long routeId, CancellationToken ct = default);
    Task AddRouteAsync(Route route, CancellationToken ct = default);

    // Trips
    Task<List<Trip>> SearchTripsAsync(long originLocationId, long destinationLocationId, DateTimeOffset windowStart, DateTimeOffset windowEnd, CancellationToken ct = default);
    Task<Trip?> GetTripWithDetailsAsync(long tripId, CancellationToken ct = default);
    Task<List<Trip>> GetAllTripsAdminAsync(CancellationToken ct = default);
    Task<Trip?> GetTripForBookingAsync(long tripId, CancellationToken ct = default);
    Task<bool> HasConflictingScheduledTripAsync(long busId, DateTimeOffset departureTime, DateTimeOffset arrivalTime, CancellationToken ct = default);
    Task AddTripAsync(Trip trip, CancellationToken ct = default);
    Task<bool> CancelTripAsync(long tripId, CancellationToken ct = default);
}
