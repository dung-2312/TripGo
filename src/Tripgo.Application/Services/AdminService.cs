using System.ComponentModel.DataAnnotations;
using Tripgo.Application.DTOs;
using Tripgo.Application.DTOs.Admin;
using Tripgo.Application.Interfaces;
using Tripgo.Application.Interfaces.Persistence;
using Tripgo.Domain.Entities;
using Tripgo.Domain.Enums;

namespace Tripgo.Application.Services;

public sealed class AdminService : IAdminService
{
    private const int PageSize = 25;
    private const int MinimumEvidenceLength = 10;

    private static readonly Dictionary<TripStatus, TripStatus[]> AllowedTripTransitions = new()
    {
        [TripStatus.Draft] = new[] { TripStatus.Scheduled },
        [TripStatus.Scheduled] = new[] { TripStatus.Boarding },
        [TripStatus.Boarding] = new[] { TripStatus.Departed },
        [TripStatus.Departed] = new[] { TripStatus.Completed }
    };

    private readonly IAdminAccessGuard _guard;
    private readonly IAdminRepository _repository;
    private readonly TripService _trips;
    private readonly IPaymentGatewayService _payments;

    public AdminService(
        IAdminAccessGuard guard,
        IAdminRepository repository,
        TripService trips,
        IPaymentGatewayService payments)
    {
        _guard = guard;
        _repository = repository;
        _trips = trips;
        _payments = payments;
    }

    private Task<AdminActor> RequireAdminAsync() => _guard.RequireRoleAsync(RoleNames.Admin);

    private Task AuditAsync(AdminActor actor, string action, string entityType, object entityId, string? details, CancellationToken ct) =>
        _repository.AddAuditAsync(new AdminAuditLog
        {
            ActorUserId = actor.UserId,
            ActorEmail = actor.Email,
            Action = action,
            EntityType = entityType,
            EntityId = entityId.ToString() ?? string.Empty,
            Details = details is { Length: > 2000 } ? details[..2000] : details,
            CreatedAt = DateTimeOffset.UtcNow
        }, ct);

    public async Task<AdminDashboardDto> GetDashboardAsync(CancellationToken ct = default)
    {
        await RequireAdminAsync();
        return await _repository.GetDashboardAsync(DateTimeOffset.UtcNow, ct);
    }

    // ---------------------------------------------------------------- Operators

    public async Task<List<AdminOperatorDto>> GetOperatorsAsync(string? search = null, CancellationToken ct = default)
    {
        await RequireAdminAsync();
        return await _repository.GetOperatorsAsync(search, ct);
    }

    public async Task<AdminResult> SaveOperatorAsync(AdminOperatorInput input, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var actor = await RequireAdminAsync();

        input.Code = input.Code?.Trim() ?? string.Empty;
        input.Name = input.Name?.Trim() ?? string.Empty;
        input.TaxCode = EmptyToNull(input.TaxCode);
        input.LegalName = EmptyToNull(input.LegalName);
        input.Phone = EmptyToNull(input.Phone);
        input.Email = EmptyToNull(input.Email);
        input.Address = EmptyToNull(input.Address);
        input.Description = EmptyToNull(input.Description);
        input.SourceReference = EmptyToNull(input.SourceReference);

        if (input.Code.Length is 0 or > 30 || input.Name.Length is 0 or > 200)
            return AdminResult.Fail("Mã nhà xe (tối đa 30 ký tự) và tên nhà xe (tối đa 200 ký tự) là bắt buộc.");
        if (input.Phone is { Length: > 20 })
            return AdminResult.Fail("Số điện thoại tối đa 20 ký tự.");
        if (input.Email != null && !new EmailAddressAttribute().IsValid(input.Email))
            return AdminResult.Fail("Email liên hệ không hợp lệ.");
        if (input.Address is { Length: > 500 } || input.LegalName is { Length: > 250 } || input.TaxCode is { Length: > 30 })
            return AdminResult.Fail("Địa chỉ tối đa 500, tên pháp lý 250 và mã số thuế 30 ký tự.");
        if (await _repository.OperatorCodeOrTaxCodeTakenAsync(input.Code, input.TaxCode, input.Id, ct))
            return AdminResult.Fail("Mã nhà xe hoặc mã số thuế đã được sử dụng.");

        var isNew = input.Id <= 0;
        if (isNew)
        {
            input.Id = await _trips.CreateOperatorAsync(new CreateOperatorDto
            {
                Code = input.Code,
                Name = input.Name,
                Hotline = input.Phone ?? string.Empty,
                Address = input.Address ?? string.Empty
            }, ct);
        }

        if (!await _repository.UpdateOperatorAsync(input, ct))
            return AdminResult.Fail("Không tìm thấy nhà xe.");

        await AuditAsync(actor, isNew ? "Operator.Create" : "Operator.Update", "Operator", input.Id, input.Code, ct);
        return AdminResult.Ok(isNew ? "Đã thêm nhà xe (chưa xác minh)." : "Đã cập nhật nhà xe.", input.Id);
    }

    public async Task<AdminResult> SetOperatorActiveAsync(long operatorId, bool isActive, CancellationToken ct = default)
    {
        var actor = await RequireAdminAsync();
        if (!await _repository.SetOperatorActiveAsync(operatorId, isActive, ct))
            return AdminResult.Fail("Không tìm thấy nhà xe.");

        await AuditAsync(actor, isActive ? "Operator.Activate" : "Operator.Deactivate", "Operator", operatorId, null, ct);
        return AdminResult.Ok(isActive ? "Đã kích hoạt nhà xe." : "Đã ngưng hoạt động nhà xe.");
    }

    public async Task<AdminResult> SetOperatorVerificationAsync(long operatorId, bool isVerified, string? evidence, CancellationToken ct = default)
    {
        var actor = await RequireAdminAsync();
        evidence = EmptyToNull(evidence);

        if (isVerified && (evidence == null || evidence.Length < MinimumEvidenceLength))
            return AdminResult.Fail("Cần ghi rõ bằng chứng xác minh (giấy phép, nguồn công bố, người xác nhận) trước khi đánh dấu đã xác minh.");
        if (evidence is { Length: > 1000 })
            return AdminResult.Fail("Bằng chứng xác minh tối đa 1000 ký tự.");

        if (!await _repository.SetOperatorVerificationAsync(operatorId, isVerified, evidence, actor.UserId, DateTimeOffset.UtcNow, ct))
            return AdminResult.Fail("Không tìm thấy nhà xe.");

        await AuditAsync(actor, isVerified ? "Operator.Verify" : "Operator.Unverify", "Operator", operatorId, evidence, ct);
        return AdminResult.Ok(isVerified ? "Đã ghi nhận xác minh nhà xe." : "Đã gỡ trạng thái xác minh.");
    }

    // ---------------------------------------------------------------- Routes

    public async Task<List<LocationDto>> GetLocationsAsync(CancellationToken ct = default)
    {
        await RequireAdminAsync();
        return await _trips.GetLocationsAsync(ct);
    }

    public async Task<List<AdminRouteDto>> GetRoutesAsync(bool includeInactive = true, CancellationToken ct = default)
    {
        await RequireAdminAsync();
        return await _repository.GetRoutesAsync(includeInactive, ct);
    }

    public async Task<AdminRouteDto?> GetRouteAsync(long routeId, CancellationToken ct = default)
    {
        await RequireAdminAsync();
        return await _repository.GetRouteAsync(routeId, ct);
    }

    public async Task<AdminResult> SaveRouteAsync(AdminRouteInput input, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var actor = await RequireAdminAsync();

        input.Code = input.Code?.Trim() ?? string.Empty;
        input.Name = input.Name?.Trim() ?? string.Empty;

        if (input.OriginLocationId == input.DestinationLocationId)
            return AdminResult.Fail("Điểm đi và điểm đến phải khác nhau.");
        if (await _repository.ActiveRouteExistsAsync(input.OperatorId, input.OriginLocationId, input.DestinationLocationId, input.Id, ct))
            return AdminResult.Fail("Nhà xe này đã có tuyến đang hoạt động cùng điểm đi và điểm đến.");
        if (input.Code.Length > 0 && await _repository.RouteCodeTakenAsync(input.Code, input.Id, ct))
            return AdminResult.Fail("Mã tuyến đã được sử dụng.");

        if (input.Id > 0)
        {
            if (input.DistanceKm <= 0 || input.EstimatedDurationMinutes <= 0 || input.Name.Length is 0 or > 200)
                return AdminResult.Fail("Tên tuyến, cự ly và thời lượng hợp lệ là bắt buộc.");
            if (!await _repository.UpdateRouteAsync(input, ct))
                return AdminResult.Fail("Không tìm thấy tuyến.");

            await AuditAsync(actor, "Route.Update", "Route", input.Id, input.Name, ct);
            return AdminResult.Ok("Đã cập nhật tuyến.", input.Id);
        }

        try
        {
            var id = await _trips.CreateRouteAsync(new CreateRouteDto
            {
                OperatorId = input.OperatorId,
                OriginLocationId = input.OriginLocationId,
                DestinationLocationId = input.DestinationLocationId,
                Code = input.Code,
                Name = input.Name,
                DistanceKm = input.DistanceKm,
                EstimatedDurationMinutes = input.EstimatedDurationMinutes
            }, ct);
            await _repository.SetRouteSourceReferenceAsync(id, EmptyToNull(input.SourceReference), ct);
            await AuditAsync(actor, "Route.Create", "Route", id, input.Name, ct);
            return AdminResult.Ok("Đã tạo tuyến. Hãy khai báo điểm đón/trả trước khi tạo chuyến.", id);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return AdminResult.Fail(exception.Message);
        }
    }

    public async Task<AdminResult> SetRouteActiveAsync(long routeId, bool isActive, CancellationToken ct = default)
    {
        var actor = await RequireAdminAsync();
        if (!isActive)
        {
            var upcoming = await _repository.CountUpcomingTripsForRouteAsync(routeId, DateTimeOffset.UtcNow, ct);
            if (upcoming > 0)
                return AdminResult.Fail($"Tuyến còn {upcoming} chuyến sắp chạy. Hãy hủy các chuyến trước khi ngưng tuyến.");
        }

        if (!await _repository.SetRouteActiveAsync(routeId, isActive, ct))
            return AdminResult.Fail("Không tìm thấy tuyến.");

        await AuditAsync(actor, isActive ? "Route.Activate" : "Route.Deactivate", "Route", routeId, null, ct);
        return AdminResult.Ok(isActive ? "Đã kích hoạt tuyến." : "Đã ngưng tuyến.");
    }

    public async Task<AdminResult> SaveRouteStopsAsync(long routeId, List<AdminRouteStopDto> stops, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(stops);
        var actor = await RequireAdminAsync();

        var problem = ValidateStops(stops);
        if (problem != null)
            return AdminResult.Fail(problem);

        var pointIds = stops.Select(s => s.PointId).Distinct().ToList();
        var activePoints = await _repository.GetActivePointIdsAsync(pointIds, ct);
        if (activePoints.Count != pointIds.Count)
            return AdminResult.Fail("Có điểm dừng không tồn tại hoặc đã ngưng hoạt động.");

        await _repository.ReplaceRouteStopsAsync(routeId, stops.Select(s => new RouteStop
        {
            RouteId = routeId,
            PointId = s.PointId,
            StopOrder = s.StopOrder,
            AllowPickup = s.AllowPickup,
            AllowDropoff = s.AllowDropoff,
            ArrivalOffsetMinutes = s.ArrivalOffsetMinutes,
            DepartureOffsetMinutes = s.DepartureOffsetMinutes
        }).ToList(), ct);

        await AuditAsync(actor, "Route.Stops", "Route", routeId, $"{stops.Count} điểm dừng", ct);
        return AdminResult.Ok("Đã lưu điểm dừng của tuyến. Chuyến đã tạo trước đó giữ nguyên điểm dừng hiện có.");
    }

    internal static string? ValidateStops(IReadOnlyList<AdminRouteStopDto> stops)
    {
        if (stops.Count < 2)
            return "Tuyến cần ít nhất một điểm đón và một điểm trả.";
        if (stops.Select(s => s.StopOrder).Distinct().Count() != stops.Count || stops.Any(s => s.StopOrder < 1))
            return "Thứ tự điểm dừng phải là số dương và không trùng nhau.";
        if (stops.Select(s => s.PointId).Distinct().Count() != stops.Count)
            return "Mỗi điểm chỉ được xuất hiện một lần trên tuyến.";
        if (stops.Any(s => !s.AllowPickup && !s.AllowDropoff))
            return "Mỗi điểm dừng phải cho phép đón hoặc trả khách.";

        var pickups = stops.Where(s => s.AllowPickup).Select(s => s.StopOrder).ToList();
        var dropoffs = stops.Where(s => s.AllowDropoff).Select(s => s.StopOrder).ToList();
        if (pickups.Count == 0 || dropoffs.Count == 0)
            return "Tuyến cần ít nhất một điểm đón và một điểm trả.";
        if (pickups.Min() >= dropoffs.Max())
            return "Điểm đón đầu tiên phải nằm trước điểm trả cuối cùng.";

        var offsets = stops.OrderBy(s => s.StopOrder)
            .Select(s => s.DepartureOffsetMinutes ?? s.ArrivalOffsetMinutes)
            .Where(offset => offset.HasValue)
            .Select(offset => offset!.Value)
            .ToList();
        if (offsets.Any(offset => offset < 0) || offsets.Zip(offsets.Skip(1)).Any(pair => pair.First > pair.Second))
            return "Mốc thời gian của điểm dừng phải tăng dần theo thứ tự tuyến.";

        return null;
    }

    public async Task<List<AdminPointDto>> GetPointsAsync(long operatorId, CancellationToken ct = default)
    {
        await RequireAdminAsync();
        return await _repository.GetPointsAsync(operatorId, ct);
    }

    public async Task<AdminResult> AddPointAsync(AdminPointInput input, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var actor = await RequireAdminAsync();

        var name = input.Name?.Trim() ?? string.Empty;
        var address = input.Address?.Trim() ?? string.Empty;
        if (input.OperatorId <= 0 || input.LocationId <= 0 || name.Length < 2 || address.Length < 5)
            return AdminResult.Fail("Nhà xe, tỉnh/thành, tên điểm và địa chỉ cụ thể là bắt buộc.");

        var id = await _repository.AddPointAsync(new PickupDropoffPoint
        {
            OperatorId = input.OperatorId,
            LocationId = input.LocationId,
            Name = name,
            Address = address,
            PointType = PointType.Both,
            IsActive = true
        }, ct);

        await AuditAsync(actor, "Point.Create", "PickupDropoffPoint", id, name, ct);
        return AdminResult.Ok("Đã thêm điểm đón/trả.", id);
    }

    // ---------------------------------------------------------------- Fleet

    public async Task<List<AdminBusDto>> GetBusesAsync(CancellationToken ct = default)
    {
        await RequireAdminAsync();
        return await _repository.GetBusesAsync(DateTimeOffset.UtcNow, ct);
    }

    public async Task<List<BusTypeDto>> GetBusTypesAsync(CancellationToken ct = default)
    {
        await RequireAdminAsync();
        return await _trips.GetBusTypesAsync(ct);
    }

    public async Task<List<AdminSeatDto>> GetBusSeatsAsync(long busId, CancellationToken ct = default)
    {
        await RequireAdminAsync();
        return await _repository.GetBusSeatsAsync(busId, ct);
    }

    public async Task<AdminResult> CreateBusAsync(CreateBusDto input, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var actor = await RequireAdminAsync();

        var plate = input.LicensePlate?.Trim() ?? string.Empty;
        if (plate.Length > 0 && await _repository.LicensePlateTakenAsync(plate, ct))
            return AdminResult.Fail("Biển số xe đã được đăng ký.");

        try
        {
            var id = await _trips.CreateBusWithLayoutAsync(input, ct);
            await AuditAsync(actor, "Bus.Create", "Bus", id, plate, ct);
            return AdminResult.Ok("Đã đăng ký xe và sơ đồ ghế.", id);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return AdminResult.Fail(exception.Message);
        }
    }

    public async Task<AdminResult> ReplaceBusLayoutAsync(long busId, string seatLayoutDefinition, CancellationToken ct = default)
    {
        var actor = await RequireAdminAsync();

        List<SeatLayoutItem> items;
        try
        {
            items = TripService.ParseSeatLayout(seatLayoutDefinition);
        }
        catch (ArgumentException exception)
        {
            return AdminResult.Fail(exception.Message);
        }

        var upcoming = await _repository.CountUpcomingTripsForBusAsync(busId, DateTimeOffset.UtcNow, ct);
        if (upcoming > 0)
            return AdminResult.Fail($"Xe còn {upcoming} chuyến sắp chạy đang dùng sơ đồ hiện tại nên không thể thay sơ đồ.");

        if (!await _repository.ReplaceBusLayoutAsync(busId, items, ct))
            return AdminResult.Fail("Không tìm thấy xe.");

        await AuditAsync(actor, "Bus.Layout", "Bus", busId, $"{items.Count} ghế", ct);
        return AdminResult.Ok("Đã thay sơ đồ ghế.");
    }

    public async Task<AdminResult> SetBusActiveAsync(long busId, bool isActive, CancellationToken ct = default)
    {
        var actor = await RequireAdminAsync();
        if (!isActive)
        {
            var upcoming = await _repository.CountUpcomingTripsForBusAsync(busId, DateTimeOffset.UtcNow, ct);
            if (upcoming > 0)
                return AdminResult.Fail($"Xe còn {upcoming} chuyến sắp chạy. Hãy đổi xe hoặc hủy chuyến trước.");
        }

        if (!await _repository.SetBusActiveAsync(busId, isActive, ct))
            return AdminResult.Fail("Không tìm thấy xe.");

        await AuditAsync(actor, isActive ? "Bus.Activate" : "Bus.Deactivate", "Bus", busId, null, ct);
        return AdminResult.Ok(isActive ? "Đã kích hoạt xe." : "Đã ngưng hoạt động xe.");
    }

    public async Task<List<AdminDriverDto>> GetDriversAsync(CancellationToken ct = default)
    {
        await RequireAdminAsync();
        return await _repository.GetDriversAsync(ct);
    }

    public async Task<AdminResult> SaveDriverAsync(AdminDriverInput input, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var actor = await RequireAdminAsync();

        input.FullName = input.FullName?.Trim() ?? string.Empty;
        input.Phone = input.Phone?.Trim() ?? string.Empty;
        input.LicenseNumber = input.LicenseNumber?.Trim() ?? string.Empty;
        if (input.OperatorId <= 0 || input.FullName.Length < 2 || input.Phone.Length < 9 || input.LicenseNumber.Length < 3)
            return AdminResult.Fail("Nhà xe, họ tên, số điện thoại và số giấy phép lái xe là bắt buộc.");
        if (await _repository.DriverLicenseTakenAsync(input.LicenseNumber, input.Id, ct))
            return AdminResult.Fail("Số giấy phép lái xe đã được khai báo.");

        var id = await _repository.SaveDriverAsync(input, ct);
        await AuditAsync(actor, input.Id > 0 ? "Driver.Update" : "Driver.Create", "Driver", id, null, ct);
        return AdminResult.Ok("Đã lưu tài xế.", id);
    }

    public async Task<AdminResult> SetDriverActiveAsync(long driverId, bool isActive, CancellationToken ct = default)
    {
        var actor = await RequireAdminAsync();
        if (!await _repository.SetDriverActiveAsync(driverId, isActive, ct))
            return AdminResult.Fail("Không tìm thấy tài xế.");

        await AuditAsync(actor, isActive ? "Driver.Activate" : "Driver.Deactivate", "Driver", driverId, null, ct);
        return AdminResult.Ok(isActive ? "Đã kích hoạt tài xế." : "Đã ngưng tài xế.");
    }

    // ---------------------------------------------------------------- Trips

    public async Task<PagedResult<AdminTripDto>> GetTripsAsync(AdminTripFilter filter, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        await RequireAdminAsync();
        filter.Page = Math.Max(1, filter.Page);
        filter.PageSize = Math.Clamp(filter.PageSize, 5, 100);
        return await _repository.GetTripsAsync(filter, DateTimeOffset.UtcNow, ct);
    }

    public async Task<AdminResult> SaveTripAsync(AdminTripInput input, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var actor = await RequireAdminAsync();

        if (input.ExpectedArrivalTime <= input.DepartureTime)
            return AdminResult.Fail("Giờ đến dự kiến phải sau giờ khởi hành.");
        if (input.DepartureTime <= DateTimeOffset.UtcNow)
            return AdminResult.Fail("Giờ khởi hành phải ở tương lai.");
        if (input.BasePrice <= 0)
            return AdminResult.Fail("Giá vé phải lớn hơn 0.");

        var route = await _repository.GetActiveRouteAsync(input.RouteId, ct);
        if (route == null)
            return AdminResult.Fail("Tuyến không tồn tại hoặc đã ngưng hoạt động.");
        if (route.Operator is not { IsActive: true })
            return AdminResult.Fail("Nhà xe của tuyến không còn hoạt động.");

        var bus = await _repository.GetActiveBusWithLayoutAsync(input.BusId, ct);
        if (bus == null)
            return AdminResult.Fail("Xe không tồn tại hoặc đã ngưng hoạt động.");
        if (bus.OperatorId != route.OperatorId)
            return AdminResult.Fail("Xe phải thuộc cùng nhà xe với tuyến.");
        if (bus.SeatLayout?.Items == null || !bus.SeatLayout.Items.Any(item => item.IsActive))
            return AdminResult.Fail("Xe chưa có sơ đồ ghế. Hãy khai báo sơ đồ trước khi tạo chuyến.");

        if (await _repository.BusHasConflictAsync(input.BusId, input.DepartureTime, input.ExpectedArrivalTime, input.Id, ct))
            return AdminResult.Fail("Xe đã có chuyến trùng thời gian. Chọn xe hoặc thời gian khác.");

        var driverProblem = await ValidateDriversAsync(input, route.OperatorId, ct);
        if (driverProblem != null)
            return AdminResult.Fail(driverProblem);

        if (input.Id > 0)
        {
            var existing = await _repository.GetTripAsync(input.Id, ct);
            if (existing == null)
                return AdminResult.Fail("Không tìm thấy chuyến.");
            if (existing.Status is not (TripStatus.Draft or TripStatus.Scheduled))
                return AdminResult.Fail("Chỉ sửa được chuyến chưa khởi hành và chưa bị hủy.");
            if (await _repository.CountActiveBookingsAsync(input.Id, ct) > 0 &&
                (existing.RouteId != input.RouteId || existing.BusId != input.BusId ||
                 existing.DepartureTime != input.DepartureTime || existing.BasePrice != input.BasePrice))
            {
                return AdminResult.Fail("Chuyến đã có đơn đặt chỗ nên không thể đổi tuyến, xe, giờ hoặc giá. Hãy hủy chuyến nếu cần thay đổi.");
            }

            if (!await _repository.UpdateTripScheduleAsync(input, ct))
                return AdminResult.Fail("Không tìm thấy chuyến.");

            await _repository.ReplaceTripDriversAsync(input.Id, input.DriverIds, ct);
            await AuditAsync(actor, "Trip.Update", "Trip", input.Id, null, ct);
            return AdminResult.Ok("Đã cập nhật chuyến.", input.Id);
        }

        if (input.OpenAsSandbox && !route.Stops.Any(stop => stop.AllowPickup) )
            return AdminResult.Fail("Tuyến chưa có điểm đón nên không thể mở bán thử nghiệm.");
        if (input.OpenAsSandbox && !route.Stops.Any(stop => stop.AllowDropoff))
            return AdminResult.Fail("Tuyến chưa có điểm trả nên không thể mở bán thử nghiệm.");

        try
        {
            var id = await _trips.CreateTripAsync(new CreateTripDto
            {
                RouteId = input.RouteId,
                BusId = input.BusId,
                OperatorId = route.OperatorId,
                DepartureTime = input.DepartureTime,
                ExpectedArrivalTime = input.ExpectedArrivalTime,
                BasePrice = input.BasePrice
            }, ct);
            await _repository.FinalizeNewTripAsync(id, input.OpenAsSandbox, ct);
            await _repository.ReplaceTripDriversAsync(id, input.DriverIds, ct);
            await AuditAsync(actor, "Trip.Create", "Trip", id, input.OpenAsSandbox ? "Sandbox" : "Reference", ct);
            return AdminResult.Ok(
                input.OpenAsSandbox ? "Đã tạo chuyến thử nghiệm (Sandbox)." : "Đã lưu lịch trình tham khảo. Chuyến này không mở bán.",
                id);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return AdminResult.Fail(exception.Message);
        }
    }

    private async Task<string?> ValidateDriversAsync(AdminTripInput input, long operatorId, CancellationToken ct)
    {
        var driverIds = input.DriverIds?.Distinct().ToList() ?? new List<long>();
        if (driverIds.Count == 0)
            return null;

        var drivers = await _repository.GetDriversByIdsAsync(driverIds, ct);
        if (drivers.Count != driverIds.Count)
            return "Có tài xế không tồn tại.";

        var departureDate = DateOnly.FromDateTime(input.DepartureTime.UtcDateTime);
        foreach (var driver in drivers)
        {
            if (!driver.IsActive)
                return $"Tài xế {driver.FullName} đã ngưng hoạt động.";
            if (driver.OperatorId != operatorId)
                return $"Tài xế {driver.FullName} không thuộc nhà xe của tuyến.";
            if (driver.LicenseExpiryDate is { } expiry && expiry < departureDate)
                return $"Giấy phép lái xe của {driver.FullName} hết hạn trước ngày khởi hành.";
            if (await _repository.DriverHasConflictAsync(driver.Id, input.DepartureTime, input.ExpectedArrivalTime, input.Id, ct))
                return $"Tài xế {driver.FullName} đã có chuyến trùng thời gian.";
        }

        return null;
    }

    public async Task<AdminResult> SetTripStatusAsync(long tripId, string status, CancellationToken ct = default)
    {
        var actor = await RequireAdminAsync();
        if (!Enum.TryParse<TripStatus>(status, out var target) || target == TripStatus.Cancelled)
            return AdminResult.Fail("Trạng thái không hợp lệ. Dùng thao tác Hủy chuyến để hủy.");

        var trip = await _repository.GetTripAsync(tripId, ct);
        if (trip == null)
            return AdminResult.Fail("Không tìm thấy chuyến.");
        if (!AllowedTripTransitions.TryGetValue(trip.Status, out var allowed) || !allowed.Contains(target))
            return AdminResult.Fail($"Không thể chuyển từ {trip.Status} sang {target}.");

        await _repository.SetTripStatusAsync(tripId, target, ct);
        await AuditAsync(actor, "Trip.Status", "Trip", tripId, $"{trip.Status} -> {target}", ct);
        return AdminResult.Ok("Đã cập nhật trạng thái chuyến.");
    }

    public async Task<AdminResult> CancelTripAsync(long tripId, string reason, CancellationToken ct = default)
    {
        var actor = await RequireAdminAsync();
        reason = reason?.Trim() ?? string.Empty;
        if (reason.Length < 5)
            return AdminResult.Fail("Vui lòng nhập lý do hủy chuyến (ít nhất 5 ký tự).");

        var existing = await _repository.GetTripAsync(tripId, ct);
        if (existing == null)
            return AdminResult.Fail("Không tìm thấy chuyến.");
        if (existing.Status is TripStatus.Departed or TripStatus.Completed)
            return AdminResult.Fail("Không thể hủy chuyến đã khởi hành hoặc đã hoàn tất.");

        var summary = await _repository.CancelTripAsync(tripId, reason, DateTimeOffset.UtcNow, ct);
        if (summary.AlreadyCancelled)
            return AdminResult.Ok("Chuyến đã được hủy trước đó.");

        var refunded = 0;
        var refundProblems = new List<string>();
        foreach (var bookingId in summary.PaidBookingIds)
        {
            var refund = await _payments.RefundBookingAsync(bookingId, $"Hủy chuyến: {reason}", ct);
            if (refund.Success) refunded++;
            else refundProblems.Add(refund.Message);
        }

        await AuditAsync(actor, "Trip.Cancel", "Trip", tripId,
            $"{reason} | Giải phóng {summary.ReleasedUnpaidBookings} đơn chưa thanh toán; hoàn {refunded}/{summary.PaidBookingIds.Count} đơn đã thanh toán.", ct);

        return refundProblems.Count == 0
            ? AdminResult.Ok($"Đã hủy chuyến. Giải phóng {summary.ReleasedUnpaidBookings} đơn chưa thanh toán, hoàn tiền {refunded} đơn.")
            : AdminResult.Fail($"Đã hủy chuyến nhưng {refundProblems.Count} đơn chưa hoàn tiền được và cần xử lý thủ công: {refundProblems[0]}");
    }

    // ---------------------------------------------------------------- Bookings

    public async Task<PagedResult<AdminBookingRowDto>> GetBookingsAsync(AdminBookingFilter filter, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        await RequireAdminAsync();
        filter.Page = Math.Max(1, filter.Page);
        filter.PageSize = Math.Clamp(filter.PageSize, 5, 100);
        return await _repository.GetBookingsAsync(filter, ct);
    }

    public async Task<AdminBookingDetailDto?> GetBookingAsync(long bookingId, bool revealContact = false, CancellationToken ct = default)
    {
        var actor = await RequireAdminAsync();
        var detail = await _repository.GetBookingAsync(bookingId, revealContact, ct);
        if (detail != null && revealContact)
        {
            // Viewing a passenger's personal data is itself an audited action.
            await AuditAsync(actor, "Booking.RevealContact", "Booking", bookingId, detail.BookingCode, ct);
        }

        return detail;
    }

    public async Task<AdminResult> CancelBookingAsync(long bookingId, string reason, CancellationToken ct = default)
    {
        var actor = await RequireAdminAsync();
        reason = reason?.Trim() ?? string.Empty;
        if (reason.Length < 5)
            return AdminResult.Fail("Vui lòng nhập lý do hủy đơn (ít nhất 5 ký tự).");

        var detail = await _repository.GetBookingAsync(bookingId, false, ct);
        if (detail == null)
            return AdminResult.Fail("Không tìm thấy đơn đặt chỗ.");

        // Payment status is never edited directly: a paid booking can only leave "Paid" through a recorded refund.
        var result = detail.PaymentStatus == nameof(PaymentStatus.Paid)
            ? await _payments.RefundBookingAsync(bookingId, reason, ct)
            : await _repository.CancelUnpaidBookingAsync(bookingId, reason, DateTimeOffset.UtcNow, ct);

        if (result.Success)
        {
            await AuditAsync(actor, "Booking.Cancel", "Booking", bookingId, $"{detail.BookingCode}: {reason}", ct);
        }

        return result;
    }

    // ---------------------------------------------------------------- Monitoring

    public async Task<PagedResult<AdminAuditRowDto>> GetAuditLogAsync(int page, CancellationToken ct = default)
    {
        await RequireAdminAsync();
        return await _repository.GetAuditLogAsync(Math.Max(1, page), PageSize, ct);
    }

    public async Task<PagedResult<AdminEmailRowDto>> GetEmailOutboxAsync(string? status, int page, CancellationToken ct = default)
    {
        await RequireAdminAsync();
        return await _repository.GetEmailOutboxAsync(status, Math.Max(1, page), PageSize, ct);
    }

    public async Task<AdminResult> RetryEmailAsync(long outboxId, CancellationToken ct = default)
    {
        var actor = await RequireAdminAsync();
        if (!await _repository.RetryFailedEmailAsync(outboxId, DateTimeOffset.UtcNow, ct))
            return AdminResult.Fail("Chỉ có thể gửi lại email đang ở trạng thái thất bại.");

        await AuditAsync(actor, "Email.Retry", "BookingConfirmationEmailOutbox", outboxId, null, ct);
        return AdminResult.Ok("Đã đưa email vào hàng đợi gửi lại.");
    }

    public async Task<PagedResult<AdminPaymentEventDto>> GetPaymentEventsAsync(bool needsReviewOnly, int page, CancellationToken ct = default)
    {
        await RequireAdminAsync();
        return await _repository.GetPaymentEventsAsync(needsReviewOnly, Math.Max(1, page), PageSize, ct);
    }

    private static string? EmptyToNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
