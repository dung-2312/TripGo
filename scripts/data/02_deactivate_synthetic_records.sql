-- ============================================================================
-- SCRIPT 02: DEACTIVATE SYNTHETIC & INVALID TRANSPORT RECORDS
-- Project: Tripgo (Phase 2.5: Verified Transport Data Preparation)
-- Mode: SAFEGUARDED / REQUIRES EXPLICIT APPROVAL AND TARGET VERIFICATION
-- Expected Affected Rows:
--   - Operators: 1 deactivated (THAOMANHUNG)
--   - Routes: 20 deactivated (IsActive = 0)
--   - Trips: 3,020 archived/deactivated (Status = 6 / Cancelled)
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
PRINT 'TRIPGO PHASE 2.5: NON-DESTRUCTIVE DEACTIVATION OF SYNTHETIC DATA';
PRINT 'Target Database Verified: ' + DB_NAME();
PRINT 'Timestamp: ' + CONVERT(NVARCHAR(30), SYSDATETIMEOFFSET(), 126);
PRINT '====================================================================';

BEGIN TRANSACTION;

BEGIN TRY
    -- 0. PRE-CHECK: VERIFY NO CUSTOMER BOOKINGS EXIST ON TRIPS TO BE CANCELLED
    IF OBJECT_ID(N'dbo.Bookings', N'U') IS NOT NULL
    BEGIN
        DECLARE @BookingsOnCancelledTrips INT = 0;
        EXEC sp_executesql N'SELECT @cnt = COUNT(*) FROM [dbo].[Bookings] b INNER JOIN [dbo].[Trips] t ON b.TripId = t.Id INNER JOIN [dbo].[Routes] r ON t.RouteId = r.Id INNER JOIN [dbo].[Operators] o ON r.OperatorId = o.Id WHERE o.Code = ''THAOMANHUNG'' OR (o.Code = ''SAOVIET'' AND (r.Code LIKE ''%SGN-DLI%'' OR r.Code LIKE ''%DLI-SGN%'' OR r.Code LIKE ''%SGN-UIH%'' OR r.Code LIKE ''%UIH-SGN%'')) OR (o.Code = ''ANPHU'' AND (r.Code LIKE ''%SGN-DLI%'' OR r.Code LIKE ''%DLI-SGN%'' OR r.Code LIKE ''%HAN-DAD%'' OR r.Code LIKE ''%DAD-HAN%'')) OR (o.Code = ''TRONGMINH'' AND (r.Code LIKE ''%SGN-NHA%'' OR r.Code LIKE ''%NHA-SGN%'' OR r.Code LIKE ''%HAN-SAP%'' OR r.Code LIKE ''%SAP-HAN%'')) OR (o.Code = ''XENHA'' AND (r.Code LIKE ''%HAN-DAD%'' OR r.Code LIKE ''%DAD-HAN%'')) OR (o.Code = ''FUTA'' AND (r.Code LIKE ''%HAN-DAD%'' OR r.Code LIKE ''%DAD-HAN%''))', N'@cnt INT OUTPUT', @cnt = @BookingsOnCancelledTrips OUTPUT;
        IF @BookingsOnCancelledTrips > 0
        BEGIN
            THROW 50001, N'ABORTING: Active customer booking(s) found on targeted trips. Deactivation cancelled.', 1;
        END
    END

    -- 1. DEACTIVATE FABRICATED OPERATOR (THAOMANHUNG)
    UPDATE [dbo].[Operators]
    SET [IsActive] = 0,
        [IsVerified] = 0
    WHERE [Code] = 'THAOMANHUNG'
      AND [IsActive] = 1;

    PRINT '>> Operator THAOMANHUNG deactivated: ' + CAST(@@ROWCOUNT AS NVARCHAR(10)) + ' row(s).';

    -- 2. IDENTIFY AND DEACTIVATE 21 INVALID CORRIDOR ROUTES
    -- Note: Foreign key FK_Trips_Routes_RouteId is NO_ACTION; changing IsActive = 0 on Routes does not break FKs.
    UPDATE r
    SET r.[IsActive] = 0
    FROM [dbo].[Routes] r
    INNER JOIN [dbo].[Operators] o ON r.OperatorId = o.Id
    WHERE (
        o.Code = 'THAOMANHUNG'
        OR (o.Code = 'SAOVIET' AND (r.Code LIKE '%SGN-DLI%' OR r.Code LIKE '%DLI-SGN%' OR r.Code LIKE '%SGN-UIH%' OR r.Code LIKE '%UIH-SGN%'))
        OR (o.Code = 'ANPHU' AND (r.Code LIKE '%SGN-DLI%' OR r.Code LIKE '%DLI-SGN%' OR r.Code LIKE '%HAN-DAD%' OR r.Code LIKE '%DAD-HAN%'))
        OR (o.Code = 'TRONGMINH' AND (r.Code LIKE '%SGN-NHA%' OR r.Code LIKE '%NHA-SGN%' OR r.Code LIKE '%HAN-SAP%' OR r.Code LIKE '%SAP-HAN%'))
        OR (o.Code = 'XENHA' AND (r.Code LIKE '%HAN-DAD%' OR r.Code LIKE '%DAD-HAN%'))
        OR (o.Code = 'FUTA' AND (r.Code LIKE '%HAN-DAD%' OR r.Code LIKE '%DAD-HAN%'))
    )
    AND r.[IsActive] = 1;

    PRINT '>> Invalid Routes deactivated: ' + CAST(@@ROWCOUNT AS NVARCHAR(10)) + ' row(s) (Expected: 20).';

    -- 3. ARCHIVE / DEACTIVATE 3,020 SYNTHETIC TRIPS
    -- Changing Status to 6 (Cancelled) excludes them from customer search queries
    -- while preserving all foreign keys and historical referential integrity.
    UPDATE t
    SET t.[Status] = 6, -- TripStatus.Cancelled
        t.[UpdatedAt] = SYSDATETIMEOFFSET()
    FROM [dbo].[Trips] t
    INNER JOIN [dbo].[Routes] r ON t.RouteId = r.Id
    WHERE r.[IsActive] = 0
      AND t.[Status] <> 6;

    PRINT '>> Invalid Trips archived/cancelled: ' + CAST(@@ROWCOUNT AS NVARCHAR(10)) + ' row(s) (Expected: 3,020).';

    -- 4. SUMMARY CHECK
    DECLARE @RemainingActiveTrips INT;
    SELECT @RemainingActiveTrips = COUNT(*)
    FROM [dbo].[Trips] t
    INNER JOIN [dbo].[Routes] r ON t.RouteId = r.Id
    WHERE r.[IsActive] = 1
      AND t.[Status] = 2; -- Scheduled

    PRINT '>> Remaining Active Scheduled Trips: ' + CAST(@RemainingActiveTrips AS NVARCHAR(10)) + ' (Belonging to 10 valid corridor routes).';

    COMMIT TRANSACTION;
    PRINT '====================================================================';
    PRINT 'DEACTIVATION TRANSACTION COMMITTED SUCCESSFULLY.';
    PRINT '====================================================================';
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0
        ROLLBACK TRANSACTION;

    PRINT '!! ERROR OCCURRED DURING DEACTIVATION: ' + ERROR_MESSAGE();
    THROW;
END CATCH;
GO
