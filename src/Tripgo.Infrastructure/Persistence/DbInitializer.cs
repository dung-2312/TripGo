using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Tripgo.Domain.Entities;
using Tripgo.Domain.Enums;

namespace Tripgo.Infrastructure.Persistence;

public static class DbInitializer
{
    public static async Task SeedAsync(AppDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.EnsureWritesTargetIsolatedTestDatabase();

        var locations = new Dictionary<string, Location>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in new[]
        {
            ("HAN", "Hà Nội", "Hà Nội"),
            ("SGN", "TP. Hồ Chí Minh", "Hồ Chí Minh"),
            ("DLI", "Đà Lạt", "Lâm Đồng"),
            ("NHA", "Nha Trang", "Khánh Hòa"),
            ("UIH", "Quy Nhơn", "Bình Định"),
            ("SAP", "Sa Pa", "Lào Cai"),
            ("DAD", "Đà Nẵng", "Đà Nẵng")
        })
        {
            var location = await context.Locations.FirstOrDefaultAsync(value => value.Code == item.Item1);
            if (location == null)
            {
                location = new Location { Code = item.Item1, Name = item.Item2, Province = item.Item3, Country = "Vietnam", IsActive = true };
                context.Locations.Add(location);
                await context.SaveChangesAsync();
            }
            locations[item.Item1] = location;
        }

        var operators = new Dictionary<string, Operator>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in new[]
        {
            ("FUTA", "Phương Trang (FUTA Bus Lines)"),
            ("THAOMANHUNG", "Thảo Mạn Hùng"),
            ("XENHA", "Xe Nhà Limousine"),
            ("SAOVIET", "Sao Việt"),
            ("TRONGMINH", "Trọng Minh Limousine"),
            ("ANPHU", "An Phú Buslines")
        })
        {
            var operatorEntity = await context.Operators.FirstOrDefaultAsync(value => value.Code == item.Item1);
            if (operatorEntity == null)
            {
                operatorEntity = new Operator
                {
                    Code = item.Item1,
                    Name = item.Item2,
                    Phone = "1900 8888",
                    IsActive = true,
                    IsVerified = true,
                    CreatedAt = DateTimeOffset.UtcNow
                };
                context.Operators.Add(operatorEntity);
                await context.SaveChangesAsync();
            }
            operators[item.Item1] = operatorEntity;
        }

        var busTypes = new Dictionary<string, BusType>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in new[]
        {
            ("Xe giường nằm tiêu chuẩn", "Giường nằm tiêu chuẩn đường dài"),
            ("Limousine Giường Phòng VIP 24", "Limousine 24 phòng đơn hai tầng"),
            ("Limousine Cặp Đôi Cung Điện VIP (20 Phòng Đôi)", "Limousine phòng đôi hai tầng; 16 cabin đôi")
        })
        {
            var busType = await context.BusTypes.FirstOrDefaultAsync(value => value.Name == item.Item1);
            if (busType == null)
            {
                busType = new BusType { Name = item.Item1, Description = item.Item2, IsActive = true };
                context.BusTypes.Add(busType);
                await context.SaveChangesAsync();
            }
            busTypes[item.Item1] = busType;
        }

        var corridors = new[]
        {
            new Corridor("SGN-DLI", "SGN", "DLI", 310m, 420, "ANPHU", "SAOVIET", "TRONGMINH"),
            new Corridor("SGN-NHA", "SGN", "NHA", 430m, 480, "FUTA", "XENHA", "TRONGMINH"),
            new Corridor("SGN-UIH", "SGN", "UIH", 650m, 660, "FUTA", "SAOVIET", "THAOMANHUNG"),
            new Corridor("HAN-SAP", "HAN", "SAP", 320m, 390, "THAOMANHUNG", "SAOVIET", "TRONGMINH"),
            new Corridor("HAN-DAD", "HAN", "DAD", 770m, 840, "ANPHU", "XENHA", "FUTA")
        };

        var shifts = new[]
        {
            (Time: new TimeSpan(7, 0, 0), Slot: "0700"),
            (Time: new TimeSpan(12, 30, 0), Slot: "1230"),
            (Time: new TimeSpan(17, 0, 0), Slot: "1700"),
            (Time: new TimeSpan(22, 30, 0), Slot: "2230")
        };

        var classes = new[]
        {
            new BusClass("STD", "Xe giường nằm tiêu chuẩn", 280000m),
            new BusClass("LUX", "Limousine Giường Phòng VIP 24", 450000m),
            new BusClass("DBL", "Limousine Cặp Đôi Cung Điện VIP (20 Phòng Đôi)", 680000m)
        };
        // Phase 2.5: Synthetic generation is disabled to prevent recreating unverified records.
        // Controlled, source-verified transport data should be introduced via verified migration scripts.
        if (!EnableSyntheticGeneration)
        {
            return;
        }

        var existingTripCodes = await context.Trips
            .AsNoTracking()
            .Select(trip => trip.TripCode)
            .ToListAsync();
        var tripCodeSet = existingTripCodes.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var tripsToSeed = new List<Trip>();

        foreach (var corridor in corridors)
        {
            foreach (var direction in new[] { (From: corridor.From, To: corridor.To), (From: corridor.To, To: corridor.From) })
            {
                foreach (var vehicleClass in classes)
                {
                    var operatorCode = vehicleClass.Code switch
                    {
                        "STD" => corridor.StandardOperator,
                        "LUX" => corridor.LimousineOperator,
                        _ => corridor.CoupleOperator
                    };

                    var operatorEntity = operators[operatorCode];
                    var routeCode = $"{operatorCode}-{direction.From}-{direction.To}";
                    var route = await context.Routes.FirstOrDefaultAsync(value => value.Code == routeCode);
                    if (route == null)
                    {
                        route = new Route
                        {
                            OperatorId = operatorEntity.Id,
                            OriginLocationId = locations[direction.From].Id,
                            DestinationLocationId = locations[direction.To].Id,
                            Code = routeCode,
                            Name = $"{locations[direction.From].Name} - {locations[direction.To].Name}",
                            DistanceKm = corridor.DistanceKm,
                            EstimatedDurationMinutes = corridor.DurationMinutes,
                            IsActive = true,
                            CreatedAt = DateTimeOffset.UtcNow
                        };
                        context.Routes.Add(route);
                        await context.SaveChangesAsync();
                    }

                    var busType = busTypes[vehicleClass.BusTypeName];
                    var seatsPerFloor = vehicleClass.Code == "DBL" ? 8 : 12;
                    for (var shiftIndex = 0; shiftIndex < shifts.Length; shiftIndex++)
                    {
                        var shift = shifts[shiftIndex];
                        var licensePlate = $"{operatorCode[..Math.Min(3, operatorCode.Length)]}{direction.From}{direction.To}{vehicleClass.Code}{shift.Slot}";
                        var bus = await context.Buses
                            .Include(value => value.SeatLayout)
                            .FirstOrDefaultAsync(value => value.LicensePlate == licensePlate);
                        if (bus == null)
                        {
                            var totalSeats = seatsPerFloor * 2;
                            var seatLayout = new SeatLayout
                            {
                                Name = $"Sơ đồ xe {licensePlate}",
                                Floors = 2,
                                TotalSeats = totalSeats,
                                IsActive = true,
                                Items = Enumerable.Range(1, totalSeats).Select(index =>
                                {
                                    var floor = index <= seatsPerFloor ? 1 : 2;
                                    var position = ((index - 1) % seatsPerFloor) + 1;
                                    var seatCode = vehicleClass.Code == "DBL"
                                        ? $"P{index:D2}"
                                        : $"{(floor == 1 ? 'A' : 'B')}{position:D2}";
                                    return new SeatLayoutItem
                                    {
                                        SeatCode = seatCode,
                                        Floor = floor,
                                        Row = (position - 1) / 2 + 1,
                                        Column = (position - 1) % 2 + 1,
                                        SeatType = vehicleClass.Code == "DBL" ? "DoubleBed" : "Bed",
                                        IsActive = true
                                    };
                                }).ToList()
                            };
                            bus = new Bus
                            {
                                OperatorId = operatorEntity.Id,
                                BusTypeId = busType.Id,
                                SeatLayout = seatLayout,
                                LicensePlate = licensePlate,
                                Name = $"{operatorEntity.Name} {vehicleClass.Code} {shift.Slot}",
                                IsActive = true,
                                CreatedAt = DateTimeOffset.UtcNow
                            };
                            context.Buses.Add(bus);
                            await context.SaveChangesAsync();
                        }

                        for (var dayOffset = 0; dayOffset <= 30; dayOffset++)
                        {
                            var serviceDate = DateTime.Today.AddDays(dayOffset);
                            var departureDateTime = serviceDate.Add(shift.Time);
                            var departure = new DateTimeOffset(DateTime.SpecifyKind(departureDateTime, DateTimeKind.Local));
                            if (departure < DateTimeOffset.Now)
                                continue;

                            var tripCode = $"{routeCode}-{serviceDate:yyMMdd}-{shift.Slot}";
                            if (!tripCodeSet.Add(tripCode))
                                continue;

                            var price = vehicleClass.BasePrice + (shiftIndex % (vehicleClass.Code == "STD" ? 3 : 4)) * 20000m;
                            tripsToSeed.Add(new Trip
                            {
                                RouteId = route.Id,
                                BusId = bus.Id,
                                TripCode = tripCode,
                                DepartureTime = departure,
                                ExpectedArrivalTime = departure.AddMinutes(corridor.DurationMinutes),
                                BasePrice = price,
                                Status = TripStatus.Scheduled,
                                CreatedAt = DateTimeOffset.UtcNow
                            });
                        }
                        await context.SaveChangesAsync();
                    }
                }
            }
        }

        if (tripsToSeed.Count > 0)
        {
            context.Trips.AddRange(tripsToSeed);
            await context.SaveChangesAsync();
        }
    }

    private static readonly bool EnableSyntheticGeneration = false;
    private sealed record Corridor(string Code, string From, string To, decimal DistanceKm, int DurationMinutes, string StandardOperator, string LimousineOperator, string CoupleOperator);
    private sealed record BusClass(string Code, string BusTypeName, decimal BasePrice);
}