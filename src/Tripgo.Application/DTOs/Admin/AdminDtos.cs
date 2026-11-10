namespace Tripgo.Application.DTOs.Admin;

public sealed class AdminResult
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
    public long? Id { get; init; }

    public static AdminResult Ok(string message, long? id = null) => new() { Success = true, Message = message, Id = id };
    public static AdminResult Fail(string message) => new() { Success = false, Message = message };
}

public sealed class PagedResult<T>
{
    public IReadOnlyList<T> Items { get; init; } = Array.Empty<T>();
    public int Page { get; init; }
    public int PageSize { get; init; }
    public int TotalCount { get; init; }
    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
}

/// <summary>Every figure is counted from stored records; null means there is nothing to base it on.</summary>
public sealed class AdminDashboardDto
{
    public int TotalBookings { get; init; }
    public int PendingBookings { get; init; }
    public int ConfirmedBookings { get; init; }
    public int CancelledBookings { get; init; }
    public int ExpiredBookings { get; init; }
    public int UpcomingTrips { get; init; }
    public int SandboxTrips { get; init; }
    public int ReferenceTrips { get; init; }
    public int CancelledTrips { get; init; }
    public int ActiveOperators { get; init; }
    public int InactiveOperators { get; init; }
    public int VerifiedOperators { get; init; }
    public int ActiveRoutes { get; init; }
    public int ActiveBuses { get; init; }
    public decimal? SandboxPaidAmount { get; init; }
    public int SandboxPaidTransactions { get; init; }
    public decimal? RealPaidAmount { get; init; }
    public int RealPaidTransactions { get; init; }
    public int EmailsPending { get; init; }
    public int EmailsFailed { get; init; }
    public int PaymentEventsNeedingReview { get; init; }
    public IReadOnlyList<AdminDashboardTopRouteDto> TopRoutes { get; init; } = Array.Empty<AdminDashboardTopRouteDto>();
    public IReadOnlyList<AdminBookingRowDto> RecentBookings { get; init; } = Array.Empty<AdminBookingRowDto>();
}

public sealed class AdminDashboardTopRouteDto
{
    public string RouteName { get; init; } = string.Empty;
    public string OperatorName { get; init; } = string.Empty;
    public int BookingCount { get; init; }
    public decimal TotalRevenue { get; init; }
}

public sealed class AdminOperatorDto
{
    public long Id { get; init; }
    public string Code { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? LegalName { get; init; }
    public string? TaxCode { get; init; }
    public string? Phone { get; init; }
    public string? Email { get; init; }
    public string? Address { get; init; }
    public string? Description { get; init; }
    public bool IsActive { get; init; }
    public bool IsVerified { get; init; }
    public string? VerificationEvidence { get; init; }
    public DateTimeOffset? VerifiedAt { get; init; }
    public string DataSource { get; init; } = string.Empty;
    public string? SourceReference { get; init; }
    public int RouteCount { get; init; }
    public int BusCount { get; init; }
}

public sealed class AdminOperatorInput
{
    public long Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? LegalName { get; set; }
    public string? TaxCode { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Address { get; set; }
    public string? Description { get; set; }
    public string? SourceReference { get; set; }
}

public sealed class AdminRouteDto
{
    public long Id { get; init; }
    public long OperatorId { get; init; }
    public string OperatorName { get; init; } = string.Empty;
    public string Code { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public long OriginLocationId { get; init; }
    public long DestinationLocationId { get; init; }
    public string OriginName { get; init; } = string.Empty;
    public string DestinationName { get; init; } = string.Empty;
    public decimal? DistanceKm { get; init; }
    public int? EstimatedDurationMinutes { get; init; }
    public bool IsActive { get; init; }
    public string DataSource { get; init; } = string.Empty;
    public string? SourceReference { get; init; }
    public int StopCount { get; init; }
    public List<AdminRouteStopDto> Stops { get; init; } = new();
}

public sealed class AdminRouteStopDto
{
    public long PointId { get; set; }
    public string PointName { get; set; } = string.Empty;
    public int StopOrder { get; set; }
    public bool AllowPickup { get; set; }
    public bool AllowDropoff { get; set; }
    public int? ArrivalOffsetMinutes { get; set; }
    public int? DepartureOffsetMinutes { get; set; }
}

public sealed class AdminRouteInput
{
    public long Id { get; set; }
    public long OperatorId { get; set; }
    public long OriginLocationId { get; set; }
    public long DestinationLocationId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public decimal DistanceKm { get; set; }
    public int EstimatedDurationMinutes { get; set; }
    public string? SourceReference { get; set; }
}

public sealed class AdminPointDto
{
    public long Id { get; init; }
    public long OperatorId { get; init; }
    public long LocationId { get; init; }
    public string LocationName { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Address { get; init; } = string.Empty;
    public bool IsActive { get; init; }
}

public sealed class AdminPointInput
{
    public long OperatorId { get; set; }
    public long LocationId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
}

public sealed class AdminBusDto
{
    public long Id { get; init; }
    public long OperatorId { get; init; }
    public string OperatorName { get; init; } = string.Empty;
    public long BusTypeId { get; init; }
    public string BusTypeName { get; init; } = string.Empty;
    public string LicensePlate { get; init; } = string.Empty;
    public string? Name { get; init; }
    public bool IsActive { get; init; }
    public int TotalSeats { get; init; }
    public int Floors { get; init; }
    public int UpcomingTrips { get; init; }
}

public sealed class AdminSeatDto
{
    public string SeatCode { get; init; } = string.Empty;
    public int Floor { get; init; }
    public int Row { get; init; }
    public int Column { get; init; }
    public string SeatType { get; init; } = string.Empty;
}

public sealed class AdminDriverDto
{
    public long Id { get; init; }
    public long OperatorId { get; init; }
    public string OperatorName { get; init; } = string.Empty;
    public string FullName { get; init; } = string.Empty;
    public string Phone { get; init; } = string.Empty;
    public string LicenseNumber { get; init; } = string.Empty;
    public string? LicenseClass { get; init; }
    public DateOnly? LicenseExpiryDate { get; init; }
    public bool IsActive { get; init; }
}

public sealed class AdminDriverInput
{
    public long Id { get; set; }
    public long OperatorId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string LicenseNumber { get; set; } = string.Empty;
    public string? LicenseClass { get; set; }
    public DateOnly? LicenseExpiryDate { get; set; }
}

public sealed class AdminTripFilter
{
    public string? Search { get; set; }
    public string? Status { get; set; }
    public bool UpcomingOnly { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 25;
}

public sealed class AdminTripDto
{
    public long Id { get; init; }
    public string TripCode { get; init; } = string.Empty;
    public long RouteId { get; init; }
    public string RouteName { get; init; } = string.Empty;
    public long OperatorId { get; init; }
    public string OperatorName { get; init; } = string.Empty;
    public long BusId { get; init; }
    public string BusLicensePlate { get; init; } = string.Empty;
    public DateTimeOffset DepartureTime { get; init; }
    public DateTimeOffset ExpectedArrivalTime { get; init; }
    public decimal BasePrice { get; init; }
    public string Status { get; init; } = string.Empty;
    public bool IsSandbox { get; init; }
    public string DataSource { get; init; } = string.Empty;
    public int TotalSeats { get; init; }
    public int BookedSeats { get; init; }
    public int ActiveBookings { get; init; }
    public List<long> DriverIds { get; init; } = new();
    public string? CancellationReason { get; init; }
}

public sealed class AdminTripInput
{
    public long Id { get; set; }
    public long RouteId { get; set; }
    public long BusId { get; set; }
    public DateTimeOffset DepartureTime { get; set; }
    public DateTimeOffset ExpectedArrivalTime { get; set; }
    public decimal BasePrice { get; set; }
    public bool OpenAsSandbox { get; set; }
    public List<long> DriverIds { get; set; } = new();
}

public sealed class AdminBookingFilter
{
    public string? Search { get; set; }
    public string? Status { get; set; }
    public string? PaymentStatus { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 25;
}

public sealed class AdminBookingRowDto
{
    public long Id { get; init; }
    public string BookingCode { get; init; } = string.Empty;
    public string RouteName { get; init; } = string.Empty;
    public DateTimeOffset DepartureTime { get; init; }
    public string MaskedPassengerName { get; init; } = string.Empty;
    public string MaskedPhone { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string PaymentStatus { get; init; } = string.Empty;
    public decimal TotalAmount { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public bool IsSandbox { get; init; }
}

public sealed class AdminBookingDetailDto
{
    public long Id { get; init; }
    public string BookingCode { get; init; } = string.Empty;
    public string RouteName { get; init; } = string.Empty;
    public string OperatorName { get; init; } = string.Empty;
    public DateTimeOffset DepartureTime { get; init; }
    public string PickupPoint { get; init; } = string.Empty;
    public string DropoffPoint { get; init; } = string.Empty;
    public List<string> SeatCodes { get; init; } = new();
    public string MaskedPassengerName { get; init; } = string.Empty;
    public string MaskedPhone { get; init; } = string.Empty;
    public string MaskedEmail { get; init; } = string.Empty;
    public string? PassengerName { get; init; }
    public string? PassengerPhone { get; init; }
    public string? PassengerEmail { get; init; }
    public bool ContactRevealed { get; init; }
    public int PassengerCount { get; init; }
    public decimal BaseAmount { get; init; }
    public decimal SurchargeAmount { get; init; }
    public decimal TotalAmount { get; init; }
    public string Status { get; init; } = string.Empty;
    public string PaymentStatus { get; init; } = string.Empty;
    public bool IsSandbox { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset ExpiresAt { get; init; }
    public DateTimeOffset? CancelledAt { get; init; }
    public string? CancellationReason { get; init; }
    public List<AdminPaymentRowDto> Payments { get; init; } = new();
}

public sealed class AdminPaymentRowDto
{
    public string TransactionCode { get; init; } = string.Empty;
    public string Provider { get; init; } = string.Empty;
    public string PaymentMethod { get; init; } = string.Empty;
    public decimal Amount { get; init; }
    public string Currency { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public bool IsSandbox { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }
    public DateTimeOffset? RefundedAt { get; init; }
}

public sealed class AdminAuditRowDto
{
    public long Id { get; init; }
    public string ActorEmail { get; init; } = string.Empty;
    public string Action { get; init; } = string.Empty;
    public string EntityType { get; init; } = string.Empty;
    public string EntityId { get; init; } = string.Empty;
    public string? Details { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}

public sealed class AdminEmailRowDto
{
    public long Id { get; init; }
    public string BookingCode { get; init; } = string.Empty;
    public string MessageType { get; init; } = string.Empty;
    public string MaskedRecipient { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public int AttemptCount { get; init; }
    public DateTimeOffset NextAttemptAt { get; init; }
    public DateTimeOffset? SentAt { get; init; }
    public string? LastError { get; init; }
    public string RecipientEmail { get; init; } = string.Empty;
    public string Payload { get; init; } = string.Empty;
}

public sealed class AdminPaymentEventDto
{
    public long Id { get; init; }
    public string Provider { get; init; } = string.Empty;
    public string? EventId { get; init; }
    public string? TransactionCode { get; init; }
    public bool SignatureValid { get; init; }
    public string Outcome { get; init; } = string.Empty;
    public decimal? ReportedAmount { get; init; }
    public string? ReportedCurrency { get; init; }
    public string? Detail { get; init; }
    public DateTimeOffset ReceivedAt { get; init; }
}

public sealed class TripCancellationSummary
{
    public bool Found { get; init; }
    public bool AlreadyCancelled { get; init; }
    public int ReleasedUnpaidBookings { get; init; }
    public List<long> PaidBookingIds { get; init; } = new();
}
