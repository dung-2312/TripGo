using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Tripgo.Domain.Entities;
using Tripgo.Domain.Enums;
using Tripgo.Domain.Interfaces;
using Tripgo.Infrastructure.Persistence;

namespace Tripgo.Infrastructure.Services
{
    public class TripService : ITripService
    {
        private readonly AppDbContext _dbContext;

        public TripService(AppDbContext dbContext)
        {
            _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        }

        public async Task<List<LocationDto>> GetLocationsAsync(CancellationToken cancellationToken = default)
        {
            return await _dbContext.Locations
                .AsNoTracking()
                .Where(l => l.IsActive)
                .OrderBy(l => l.Name)
                .Select(l => new LocationDto
                {
                    Id = l.Id,
                    Code = l.Code,
                    Name = l.Name,
                    Province = l.Province,
                    Country = l.Country,
                    IsActive = l.IsActive
                })
                .ToListAsync(cancellationToken);
        }

        public async Task<List<TripSearchResultDto>> SearchTripsAsync(TripSearchRequestDto request, CancellationToken cancellationToken = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));

            var date = request.DepartureDate;
            var startLocal = new DateTime(date.Year, date.Month, date.Day, 0, 0, 0, DateTimeKind.Local);
            var start = new DateTimeOffset(startLocal);
            var end = start.AddDays(1).AddTicks(-1);

            var query = _dbContext.Trips
                .AsNoTracking()
                .Include(t => t.Route)
                    .ThenInclude(r => r.Operator)
                .Include(t => t.Bus)
                    .ThenInclude(b => b.BusType)
                .Include(t => t.Bus)
                    .ThenInclude(b => b.SeatLayout)
                        .ThenInclude(sl => sl.Items)
                .Where(t =>
                    t.Route.OriginLocationId == request.OriginLocationId &&
                    t.Route.DestinationLocationId == request.DestinationLocationId &&
                    t.DepartureTime >= start &&
                    t.DepartureTime <= end &&
                    t.Status == TripStatus.Scheduled);

            var results = await query
                .Select(t => new TripSearchResultDto
                {
                    Id = t.Id,
                    RouteName = t.Route.Name,
                    OperatorName = t.Route.Operator.Name,
                    BusTypeName = t.Bus.BusType.Name,
                    DepartureTime = t.DepartureTime,
                    ArrivalTime = t.ExpectedArrivalTime,
                    BasePrice = t.BasePrice,
                    TotalSeats = t.Bus.SeatLayout.TotalSeats,
                    AvailableSeats = t.Bus.SeatLayout.TotalSeats
                })
                .ToListAsync(cancellationToken);

            return results;
        }

        public async Task<TripDetailDto?> GetTripDetailsAsync(long tripId, CancellationToken cancellationToken = default)
        {
            var trip = await _dbContext.Trips
                .AsNoTracking()
                .Where(t => t.Id == tripId)
                .Include(t => t.Route)
                    .ThenInclude(r => r.Operator)
                .Include(t => t.Bus)
                    .ThenInclude(b => b.BusType)
                .Include(t => t.Bus)
                    .ThenInclude(b => b.SeatLayout)
                        .ThenInclude(sl => sl.Items)
                .FirstOrDefaultAsync(cancellationToken);

            if (trip == null) return null;

            var seatItems = trip.Bus.SeatLayout.Items
                .OrderBy(i => i.Floor)
                .ThenBy(i => i.Row)
                .ThenBy(i => i.Column)
                .Select(i => new SeatLayoutItemDto
                {
                    Id = i.Id,
                    SeatCode = i.SeatCode,
                    Floor = i.Floor,
                    Row = i.Row,
                    Column = i.Column,
                    SeatType = i.SeatType,
                    IsAvailable = i.IsActive
                })
                .ToList();

            var dto = new TripDetailDto
            {
                Id = trip.Id,
                RouteName = trip.Route.Name,
                OperatorName = trip.Route.Operator.Name,
                BusTypeName = trip.Bus.BusType.Name,
                BusLicensePlate = trip.Bus.LicensePlate,
                DepartureTime = trip.DepartureTime,
                ArrivalTime = trip.ExpectedArrivalTime,
                BasePrice = trip.BasePrice,
                TotalSeats = trip.Bus.SeatLayout.TotalSeats,
                Seats = seatItems
            };

            return dto;
        }
    }
}