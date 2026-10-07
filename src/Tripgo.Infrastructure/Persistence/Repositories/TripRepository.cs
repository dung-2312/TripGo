using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Tripgo.Application.Interfaces.Persistence;
using Tripgo.Domain.Entities;
using Tripgo.Domain.Enums;
using Tripgo.Infrastructure.Persistence;

namespace Tripgo.Infrastructure.Persistence.Repositories;

public class TripRepository : ITripRepository
{
    private readonly AppDbContext _context;

    public TripRepository(AppDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<List<Location>> GetActiveLocationsAsync(CancellationToken ct = default)
    {
        return await _context.Locations
            .AsNoTracking()
            .Where(l => l.IsActive)
            .OrderBy(l => l.Name)
            .ToListAsync(ct);
    }

    public async Task<List<Operator>> GetActiveOperatorsAsync(CancellationToken ct = default)
    {
        return await _context.Operators
            .AsNoTracking()
            .Where(o => o.IsActive)
            .ToListAsync(ct);
    }

    public async Task<bool> OperatorExistsAndActiveAsync(long id, CancellationToken ct = default)
    {
        return await _context.Operators
            .AnyAsync(o => o.Id == id && o.IsActive, ct);
    }

    public async Task AddOperatorAsync(Operator operatorEntity, CancellationToken ct = default)
    {
        _context.Operators.Add(operatorEntity);
        await _context.SaveChangesAsync(ct);
    }

    public async Task<List<BusType>> GetActiveBusTypesAsync(CancellationToken ct = default)
    {
        return await _context.BusTypes
            .AsNoTracking()
            .Where(type => type.IsActive)
            .OrderBy(type => type.Name)
            .ToListAsync(ct);
    }

    public async Task<BusType?> GetActiveBusTypeByIdAsync(long id, CancellationToken ct = default)
    {
        return await _context.BusTypes
            .Where(type => type.Id == id && type.IsActive)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<List<Bus>> GetActiveBusesByOperatorAsync(long operatorId, CancellationToken ct = default)
    {
        return await _context.Buses
            .AsNoTracking()
            .Include(b => b.Operator)
            .Include(b => b.BusType)
            .Include(b => b.SeatLayout)
            .Where(b => b.OperatorId == operatorId && b.IsActive)
            .ToListAsync(ct);
    }

    public async Task<bool> BusExistsAndActiveAsync(long id, CancellationToken ct = default)
    {
        return await _context.Buses
            .AnyAsync(b => b.Id == id && b.IsActive, ct);
    }

    public async Task<long?> GetBusOperatorIdAsync(long busId, CancellationToken ct = default)
    {
        return await _context.Buses
            .Where(bus => bus.Id == busId && bus.IsActive)
            .Select(bus => (long?)bus.OperatorId)
            .FirstOrDefaultAsync(ct);
    }

    public async Task AddBusAsync(Bus bus, CancellationToken ct = default)
    {
        _context.Buses.Add(bus);
        await _context.SaveChangesAsync(ct);
    }

    public async Task<List<Route>> GetActiveRoutesAsync(CancellationToken ct = default)
    {
        return await _context.Routes
            .AsNoTracking()
            .Include(r => r.OriginLocation)
            .Include(r => r.DestinationLocation)
            .Where(r => r.IsActive)
            .ToListAsync(ct);
    }

    public async Task<long?> GetRouteOperatorIdAsync(long routeId, CancellationToken ct = default)
    {
        return await _context.Routes
            .Where(route => route.Id == routeId && route.IsActive)
            .Select(route => (long?)route.OperatorId)
            .FirstOrDefaultAsync(ct);
    }

    public async Task AddRouteAsync(Route route, CancellationToken ct = default)
    {
        _context.Routes.Add(route);
        await _context.SaveChangesAsync(ct);
    }

    public async Task<List<Trip>> SearchTripsAsync(
        long originLocationId,
        long destinationLocationId,
        DateTimeOffset windowStart,
        DateTimeOffset windowEnd,
        CancellationToken ct = default)
    {
        return await _context.Trips
            .AsNoTracking()
            .Include(t => t.Route)
                .ThenInclude(r => r.Operator)
            .Include(t => t.Bus)
                .ThenInclude(b => b.BusType)
            .Include(t => t.Bus)
                .ThenInclude(b => b.SeatLayout)
            .Where(t =>
                t.Route.OriginLocationId == originLocationId &&
                t.Route.DestinationLocationId == destinationLocationId &&
                t.DepartureTime >= windowStart &&
                t.DepartureTime <= windowEnd &&
                t.DepartureTime >= DateTimeOffset.Now &&
                t.Status == TripStatus.Scheduled)
            .ToListAsync(ct);
    }

    public async Task<Trip?> GetTripWithDetailsAsync(long tripId, CancellationToken ct = default)
    {
        return await _context.Trips
            .AsNoTracking()
            .Where(t => t.Id == tripId)
            .Include(t => t.Route)
                .ThenInclude(r => r.Operator)
            .Include(t => t.Bus)
                .ThenInclude(b => b.BusType)
            .Include(t => t.Bus)
                .ThenInclude(b => b.SeatLayout)
                    .ThenInclude(sl => sl.Items)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<List<Trip>> GetAllTripsAdminAsync(CancellationToken ct = default)
    {
        return await _context.Trips
            .AsNoTracking()
            .Include(t => t.Route)
                .ThenInclude(r => r.Operator)
            .Include(t => t.Bus)
                .ThenInclude(b => b.BusType)
            .Include(t => t.Bus)
                .ThenInclude(b => b.SeatLayout)
            .OrderByDescending(t => t.DepartureTime)
            .ToListAsync(ct);
    }

    public async Task<Trip?> GetTripForBookingAsync(long tripId, CancellationToken ct = default)
    {
        return await _context.Trips
            .AsNoTracking()
            .Include(item => item.Route).ThenInclude(route => route.Operator)
            .Include(item => item.Bus).ThenInclude(bus => bus.BusType)
            .Include(item => item.Bus).ThenInclude(bus => bus.SeatLayout).ThenInclude(layout => layout.Items)
            .FirstOrDefaultAsync(item => item.Id == tripId && item.Status == TripStatus.Scheduled, ct);
    }

    public async Task AddTripAsync(Trip trip, CancellationToken ct = default)
    {
        _context.Trips.Add(trip);
        await _context.SaveChangesAsync(ct);
    }

    public async Task<bool> CancelTripAsync(long tripId, CancellationToken ct = default)
    {
        var trip = await _context.Trips.FirstOrDefaultAsync(item => item.Id == tripId, ct);
        if (trip == null)
            return false;

        trip.Status = TripStatus.Cancelled;
        trip.UpdatedAt = DateTimeOffset.UtcNow;
        await _context.SaveChangesAsync(ct);
        return true;
    }
}
