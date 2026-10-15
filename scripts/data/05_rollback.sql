-- ============================================================================
-- SCRIPT 05: ROLLBACK PROCEDURE FOR PHASE 2.5 DATA CHANGES
-- Project: Tripgo (Phase 2.5: Verified Transport Data Preparation)
-- Mode: SAFEGUARDED / EMERGENCY ROLLBACK PROCEDURE
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
PRINT 'TRIPGO PHASE 2.5: EMERGENCY DATA ROLLBACK PROCEDURE';
PRINT 'Target Database Verified: ' + DB_NAME();
PRINT 'Timestamp: ' + CONVERT(NVARCHAR(30), SYSDATETIMEOFFSET(), 126);
PRINT '====================================================================';

IF OBJECT_ID(N'dbo.Trips_Backup_Phase25', N'U') IS NULL OR
   OBJECT_ID(N'dbo.Routes_Backup_Phase25', N'U') IS NULL OR
   OBJECT_ID(N'dbo.Operators_Backup_Phase25', N'U') IS NULL
BEGIN
    PRINT '!! ABORTING: One or more backup tables (Trips_Backup_Phase25, Routes_Backup_Phase25, Operators_Backup_Phase25) do not exist!';
    RETURN;
END;

BEGIN TRANSACTION;

BEGIN TRY
    -- 1. ROLLBACK OPERATORS TABLE
    UPDATE o
    SET o.[IsActive] = b.[IsActive],
        o.[IsVerified] = b.[IsVerified]
    FROM [dbo].[Operators] o
    INNER JOIN [dbo].[Operators_Backup_Phase25] b ON o.Id = b.Id;

    PRINT '>> Operators restored from backup: ' + CAST(@@ROWCOUNT AS NVARCHAR(10)) + ' row(s).';

    -- 2. ROLLBACK ROUTES TABLE
    UPDATE r
    SET r.[IsActive] = b.[IsActive]
    FROM [dbo].[Routes] r
    INNER JOIN [dbo].[Routes_Backup_Phase25] b ON r.Id = b.Id;

    PRINT '>> Routes restored from backup: ' + CAST(@@ROWCOUNT AS NVARCHAR(10)) + ' row(s).';

    -- 3. ROLLBACK TRIPS STATUS
    UPDATE t
    SET t.[Status] = b.[Status],
        t.[UpdatedAt] = b.[UpdatedAt]
    FROM [dbo].[Trips] t
    INNER JOIN [dbo].[Trips_Backup_Phase25] b ON t.Id = b.Id;

    PRINT '>> Trips restored from backup: ' + CAST(@@ROWCOUNT AS NVARCHAR(10)) + ' row(s).';

    -- 4. RESTORE ROUTESTOPS AND PICKUPDROPOFFPOINTS FROM BACKUP
    -- Correct referential integrity order:
    -- (a) Delete children (RouteStops), (b) Delete parents (PickupDropoffPoints),
    -- (c) Restore parents (PickupDropoffPoints), (d) Restore children (RouteStops).
    DELETE FROM [dbo].[RouteStops];
    DELETE FROM [dbo].[PickupDropoffPoints];

    DECLARE @RestoredPoints INT = 0, @RestoredStops INT = 0;

    IF OBJECT_ID(N'dbo.PickupDropoffPoints_Backup_Phase25', N'U') IS NOT NULL
    BEGIN
        IF EXISTS (SELECT 1 FROM [dbo].[PickupDropoffPoints_Backup_Phase25])
        BEGIN
            SET IDENTITY_INSERT [dbo].[PickupDropoffPoints] ON;
            INSERT INTO [dbo].[PickupDropoffPoints] (Id, OperatorId, LocationId, Name, Address, Latitude, Longitude, PointType, IsActive)
            SELECT Id, OperatorId, LocationId, Name, Address, Latitude, Longitude, PointType, IsActive
            FROM [dbo].[PickupDropoffPoints_Backup_Phase25];
            SELECT @RestoredPoints = @@ROWCOUNT;
            SET IDENTITY_INSERT [dbo].[PickupDropoffPoints] OFF;
        END
    END
    PRINT '>> PickupDropoffPoints restored from preflight backup: ' + CAST(@RestoredPoints AS NVARCHAR(10)) + ' row(s).';

    IF OBJECT_ID(N'dbo.RouteStops_Backup_Phase25', N'U') IS NOT NULL
    BEGIN
        IF EXISTS (SELECT 1 FROM [dbo].[RouteStops_Backup_Phase25])
        BEGIN
            SET IDENTITY_INSERT [dbo].[RouteStops] ON;
            INSERT INTO [dbo].[RouteStops] (Id, RouteId, PointId, StopOrder, ArrivalOffsetMinutes, DepartureOffsetMinutes, AllowPickup, AllowDropoff)
            SELECT Id, RouteId, PointId, StopOrder, ArrivalOffsetMinutes, DepartureOffsetMinutes, AllowPickup, AllowDropoff
            FROM [dbo].[RouteStops_Backup_Phase25];
            SELECT @RestoredStops = @@ROWCOUNT;
            SET IDENTITY_INSERT [dbo].[RouteStops] OFF;
        END
    END
    PRINT '>> RouteStops restored from preflight backup: ' + CAST(@RestoredStops AS NVARCHAR(10)) + ' row(s).';

    COMMIT TRANSACTION;
    PRINT '====================================================================';
    PRINT 'ROLLBACK COMPLETED SUCCESSFULLY. SYSTEM RETURNED TO BASELINE.';
    PRINT '====================================================================';
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0
        ROLLBACK TRANSACTION;

    PRINT '!! ERROR OCCURRED DURING ROLLBACK: ' + ERROR_MESSAGE();
    THROW;
END CATCH;
GO
