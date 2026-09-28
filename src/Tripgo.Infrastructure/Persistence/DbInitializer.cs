using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Tripgo.Domain.Entities;
using Tripgo.Domain.Enums;

namespace Tripgo.Infrastructure.Persistence
{
    public static class DbInitializer
    {
        public static async Task SeedAsync(AppDbContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            // Exit early if key data already exists
            if (await context.Locations.AnyAsync() ||
                await context.Operators.AnyAsync() ||
                await context.Trips.AnyAsync())
            {
                return;
            }

            // 1. Locations
            var hanoi = new Location
            {
                Code = "HN",
                Name = "Hà Nội",
                Province = "Hà Nội",
                Country = "Vietnam",
                IsActive = true
            };

            var hcm = new Location
            {
                Code = "HCM",
                Name = "TP. Hồ Chí Minh",
                Province = "Hồ Chí Minh",
                Country = "Vietnam",
                IsActive = true
            };

            await context.Locations.AddRangeAsync(hanoi, hcm);
            await context.SaveChangesAsync();

            // 2. Operator
            var op = new Operator
            {
                Code = "FUTA",
                Name = "Phương Trang FUTA Bus Lines",
                IsActive = true,
                IsVerified = true,
                CreatedAt = DateTimeOffset.UtcNow
            };

            await context.Operators.AddAsync(op);
            await context.SaveChangesAsync();

            // 3. SeatLayout
            var seatLayout = new SeatLayout
            {
                Name = "2-floor 24-bed layout",
                Floors = 2,
                TotalSeats = 24,
                IsActive = true
            };

            await context.SeatLayouts.AddAsync(seatLayout);
            await context.SaveChangesAsync();

            // 4. SeatLayoutItems (12 seats per floor: rows 1..6, cols 1..2)
            var items = new List<SeatLayoutItem>();
            for (int floor = 1; floor <= seatLayout.Floors; floor++)
            {
                for (int row = 1; row <= 6; row++)
                {
                    for (int col = 1; col <= 2; col++)
                    {
                        items.Add(new SeatLayoutItem
                        {
                            SeatLayoutId = seatLayout.Id,
                            SeatCode = $"F{floor}-R{row}C{col}",
                            Floor = floor,
                            Row = row,
                            Column = col,
                            SeatType = "Bed",
                            IsActive = true
                        });
                    }
                }
            }

            await context.SeatLayoutItems.AddRangeAsync(items);
            await context.SaveChangesAsync();

            // 5. BusType
            var busType = new BusType
            {
                Name = "Giường nằm cao cấp 24 phòng",
                Description = "Giường nằm cao cấp, 2 tầng, 24 phòng/giường",
                IsActive = true
            };

            await context.BusTypes.AddAsync(busType);
            await context.SaveChangesAsync();

            // 6. Bus
            var bus = new Bus
            {
                OperatorId = op.Id,
                BusTypeId = busType.Id,
                SeatLayoutId = seatLayout.Id,
                LicensePlate = "29B-123.45",
                Name = "FUTA 24-bed",
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow
            };

            await context.Buses.AddAsync(bus);
            await context.SaveChangesAsync();

            // 7. Route connecting HN -> HCM
            var route = new Route
            {
                OperatorId = op.Id,
                OriginLocationId = hanoi.Id,
                DestinationLocationId = hcm.Id,
                Code = "HN-HCM-01",
                Name = "Hà Nội - TP. Hồ Chí Minh",
                DistanceKm = 1700m,
                EstimatedDurationMinutes = 1800,
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow
            };

            await context.Routes.AddAsync(route);
            await context.SaveChangesAsync();

            // 8. Trips: today, tomorrow, day after tomorrow at 08:00 local time
            var now = DateTime.Now;
            var departBase = new DateTime(now.Year, now.Month, now.Day, 8, 0, 0, DateTimeKind.Local);

            var departures = new[]
            {
                new DateTimeOffset(departBase),
                new DateTimeOffset(departBase.AddDays(1)),
                new DateTimeOffset(departBase.AddDays(2))
            };

            var trips = new List<Trip>();
            for (int i = 0; i < departures.Length; i++)
            {
                var dep = departures[i];
                var trip = new Trip
                {
                    RouteId = route.Id,
                    BusId = bus.Id,
                    TripCode = $"FUTA-{route.Id}-{i + 1}",
                    DepartureTime = dep,
                    ExpectedArrivalTime = dep.AddHours(30),
                    BasePrice = 500_000m,
                    Status = TripStatus.Scheduled,
                    CreatedAt = DateTimeOffset.UtcNow
                };

                trips.Add(trip);
            }

            await context.Trips.AddRangeAsync(trips);
            await context.SaveChangesAsync();
        }
    }
}   