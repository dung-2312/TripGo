-- ============================================================================
-- SCRIPT 01: PREFLIGHT VALIDATION AND SNAPSHOT BACKUP
-- Project: Tripgo (Phase 2.5: Verified Transport Data Preparation)
-- Mode: SAFEGUARDED / AUDIT & BACKUP SNAPSHOT
-- ============================================================================

SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;

-- ============================================================================
-- EXECUTION SAFEGUARD (DELIBERATE APPROVAL & TARGET VERIFICATION REQUIRED)
-- ============================================================================
DECLARE @CONFIRM_EXECUTION BIT = 0; -- SAFETY LOCK: Must be explicitly changed to 1 to run
DECLARE @EXPECTED_DATABASE SYSNAME = N'TripgoDb_IsolatedTest'; -- Must match DB_NAME() exactly

IF @CONFIRM_EXECUTION <> 1
BEGIN
    RAISERROR(N'EXECUTION HALTED: Accidental execution safeguard active. To proceed, review operations, set @CONFIRM_EXECUTION = 1, and set @EXPECTED_DATABASE to the intended DB_NAME().', 16, 1);
    RETURN;
END;

IF DB_NAME() <> @EXPECTED_DATABASE
BEGIN
    DECLARE @TargetError NVARCHAR(256) = FORMATMESSAGE(N'TARGET VERIFICATION FAILED: Current database context is [%s], but expected target was [%s]. Aborting.', DB_NAME(), @EXPECTED_DATABASE);
    RAISERROR(@TargetError, 16, 1);
    RETURN;
END;

PRINT '====================================================================';
PRINT 'TRIPGO PHASE 2.5: PREFLIGHT AUDIT & BACKUP SNAPSHOT';
PRINT 'Target Database Verified: ' + DB_NAME();
PRINT 'Timestamp: ' + CONVERT(NVARCHAR(30), SYSDATETIMEOFFSET(), 126);
PRINT '====================================================================';

-- 1. PREFLIGHT METRIC CHECKS
DECLARE @TripCount INT, @RouteCount INT, @OperatorCount INT, @LocationCount INT;
DECLARE @BusCount INT, @PointCount INT, @RouteStopCount INT, @TripStopCount INT;

SELECT @TripCount = COUNT(*) FROM [dbo].[Trips];
SELECT @RouteCount = COUNT(*) FROM [dbo].[Routes];
SELECT @OperatorCount = COUNT(*) FROM [dbo].[Operators];
SELECT @LocationCount = COUNT(*) FROM [dbo].[Locations];
SELECT @BusCount = COUNT(*) FROM [dbo].[Buses];
SELECT @PointCount = COUNT(*) FROM [dbo].[PickupDropoffPoints];
SELECT @RouteStopCount = COUNT(*) FROM [dbo].[RouteStops];
SELECT @TripStopCount = COUNT(*) FROM [dbo].[TripStops];

PRINT 'PREFLIGHT CURRENT ROW COUNTS:';
PRINT '  - Trips:                ' + CAST(@TripCount AS NVARCHAR(10)) + ' (Baseline expectation: 4,533)';
PRINT '  - Routes:               ' + CAST(@RouteCount AS NVARCHAR(10)) + ' (Baseline expectation: 31)';
PRINT '  - Operators:            ' + CAST(@OperatorCount AS NVARCHAR(10)) + ' (Baseline expectation: 6)';
PRINT '  - Locations:            ' + CAST(@LocationCount AS NVARCHAR(10)) + ' (Baseline expectation: 9)';
PRINT '  - Buses:                ' + CAST(@BusCount AS NVARCHAR(10)) + ' (Baseline expectation: 122)';
PRINT '  - PickupDropoffPoints:  ' + CAST(@PointCount AS NVARCHAR(10)) + ' (Baseline expectation: 0)';
PRINT '  - RouteStops:           ' + CAST(@RouteStopCount AS NVARCHAR(10)) + ' (Baseline expectation: 0)';
PRINT '  - TripStops:            ' + CAST(@TripStopCount AS NVARCHAR(10)) + ' (Baseline expectation: 0)';

-- 2. IDENTIFY INVALID OPERATOR-ROUTE COMBINATIONS
DECLARE @InvalidTripCount INT, @InvalidRouteCount INT;

SELECT @InvalidRouteCount = COUNT(*)
FROM [dbo].[Routes] r
INNER JOIN [dbo].[Operators] o ON r.OperatorId = o.Id
WHERE o.Code = 'THAOMANHUNG'
   OR (o.Code = 'SAOVIET' AND (r.Code LIKE '%SGN-DLI%' OR r.Code LIKE '%DLI-SGN%' OR r.Code LIKE '%SGN-UIH%' OR r.Code LIKE '%UIH-SGN%'))
   OR (o.Code = 'ANPHU' AND (r.Code LIKE '%SGN-DLI%' OR r.Code LIKE '%DLI-SGN%' OR r.Code LIKE '%HAN-DAD%' OR r.Code LIKE '%DAD-HAN%'))
   OR (o.Code = 'TRONGMINH' AND (r.Code LIKE '%SGN-NHA%' OR r.Code LIKE '%NHA-SGN%' OR r.Code LIKE '%HAN-SAP%' OR r.Code LIKE '%SAP-HAN%'))
   OR (o.Code = 'XENHA' AND (r.Code LIKE '%HAN-DAD%' OR r.Code LIKE '%DAD-HAN%'))
   OR (o.Code = 'FUTA' AND (r.Code LIKE '%HAN-DAD%' OR r.Code LIKE '%DAD-HAN%'));

SELECT @InvalidTripCount = COUNT(*)
FROM [dbo].[Trips] t
INNER JOIN [dbo].[Routes] r ON t.RouteId = r.Id
INNER JOIN [dbo].[Operators] o ON r.OperatorId = o.Id
WHERE o.Code = 'THAOMANHUNG'
   OR (o.Code = 'SAOVIET' AND (r.Code LIKE '%SGN-DLI%' OR r.Code LIKE '%DLI-SGN%' OR r.Code LIKE '%SGN-UIH%' OR r.Code LIKE '%UIH-SGN%'))
   OR (o.Code = 'ANPHU' AND (r.Code LIKE '%SGN-DLI%' OR r.Code LIKE '%DLI-SGN%' OR r.Code LIKE '%HAN-DAD%' OR r.Code LIKE '%DAD-HAN%'))
   OR (o.Code = 'TRONGMINH' AND (r.Code LIKE '%SGN-NHA%' OR r.Code LIKE '%NHA-SGN%' OR r.Code LIKE '%HAN-SAP%' OR r.Code LIKE '%SAP-HAN%'))
   OR (o.Code = 'XENHA' AND (r.Code LIKE '%HAN-DAD%' OR r.Code LIKE '%DAD-HAN%'))
   OR (o.Code = 'FUTA' AND (r.Code LIKE '%HAN-DAD%' OR r.Code LIKE '%DAD-HAN%'));

PRINT '--------------------------------------------------------------------';
PRINT 'INVALID COMBINATIONS DETECTED:';
PRINT '  - Invalid Routes to Deactivate: ' + CAST(@InvalidRouteCount AS NVARCHAR(10)) + ' (Expectation: 20 corridor routes; HN-HCM-01 preserved)';
PRINT '  - Invalid Trips to Deactivate:  ' + CAST(@InvalidTripCount AS NVARCHAR(10)) + ' (Expectation: 3,020 trips; 1,513 active trips preserved)';
PRINT '--------------------------------------------------------------------';

-- 3. CREATE SAFE BACKUP SNAPSHOT TABLES (IDEMPOTENT)
IF OBJECT_ID(N'dbo.Trips_Backup_Phase25', N'U') IS NULL
BEGIN
    SELECT * INTO [dbo].[Trips_Backup_Phase25] FROM [dbo].[Trips];
    PRINT '>> Created backup snapshot table: dbo.Trips_Backup_Phase25';
END
ELSE
BEGIN
    PRINT '>> dbo.Trips_Backup_Phase25 already exists. Skipping snapshot creation.';
END

IF OBJECT_ID(N'dbo.Routes_Backup_Phase25', N'U') IS NULL
BEGIN
    SELECT * INTO [dbo].[Routes_Backup_Phase25] FROM [dbo].[Routes];
    PRINT '>> Created backup snapshot table: dbo.Routes_Backup_Phase25';
END
ELSE
BEGIN
    PRINT '>> dbo.Routes_Backup_Phase25 already exists. Skipping snapshot creation.';
END

IF OBJECT_ID(N'dbo.Operators_Backup_Phase25', N'U') IS NULL
BEGIN
    SELECT * INTO [dbo].[Operators_Backup_Phase25] FROM [dbo].[Operators];
    PRINT '>> Created backup snapshot table: dbo.Operators_Backup_Phase25';
END
ELSE
BEGIN
    PRINT '>> dbo.Operators_Backup_Phase25 already exists. Skipping snapshot creation.';
END

IF OBJECT_ID(N'dbo.PickupDropoffPoints_Backup_Phase25', N'U') IS NULL
BEGIN
    SELECT * INTO [dbo].[PickupDropoffPoints_Backup_Phase25] FROM [dbo].[PickupDropoffPoints];
    PRINT '>> Created backup snapshot table: dbo.PickupDropoffPoints_Backup_Phase25';
END
ELSE
BEGIN
    PRINT '>> dbo.PickupDropoffPoints_Backup_Phase25 already exists. Skipping snapshot creation.';
END

IF OBJECT_ID(N'dbo.RouteStops_Backup_Phase25', N'U') IS NULL
BEGIN
    SELECT * INTO [dbo].[RouteStops_Backup_Phase25] FROM [dbo].[RouteStops];
    PRINT '>> Created backup snapshot table: dbo.RouteStops_Backup_Phase25';
END
ELSE
BEGIN
    PRINT '>> dbo.RouteStops_Backup_Phase25 already exists. Skipping snapshot creation.';
END

PRINT '====================================================================';
PRINT 'PREFLIGHT AND BACKUP SNAPSHOT COMPLETED SUCCESSFULLY.';
PRINT '====================================================================';
GO
