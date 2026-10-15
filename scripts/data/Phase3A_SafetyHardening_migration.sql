-- ============================================================================
-- TRIPGO DATABASE MIGRATION SCRIPT: Phase3A_BookingSafetyHardening
-- Target Database: Verify target before executing.
-- ============================================================================
DECLARE @CONFIRM_EXECUTION BIT = 0; -- SAFETY LOCK: Must be explicitly set to 1 to execute
DECLARE @EXPECTED_DATABASE NVARCHAR(128) = N'TripgoDb_IsolatedTest';

IF @CONFIRM_EXECUTION <> 1
BEGIN
    RAISERROR(N'SAFETY ABORT: Accidental execution safeguard active. To proceed, set @CONFIRM_EXECUTION = 1.', 16, 1);
    SET NOEXEC ON;
END;

IF DB_NAME() <> @EXPECTED_DATABASE
BEGIN
    RAISERROR(N'SAFETY ABORT: Script target verification failed. Expected [%s], current [%s].', 16, 1, @EXPECTED_DATABASE, DB_NAME());
    SET NOEXEC ON;
END;
GO

SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008191552_Phase3A_BookingSafetyHardening'
)
BEGIN
    DROP INDEX [IX_BookingSeats_TripSeatId] ON [BookingSeats];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008191552_Phase3A_BookingSafetyHardening'
)
BEGIN
    ALTER TABLE [TripSeats] ADD [RowVersion] rowversion NOT NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008191552_Phase3A_BookingSafetyHardening'
)
BEGIN
    ALTER TABLE [BookingSeats] ADD [IsActive] bit NOT NULL DEFAULT CAST(1 AS bit);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008191552_Phase3A_BookingSafetyHardening'
)
BEGIN
    ALTER TABLE [Bookings] ADD [HoldToken] nvarchar(100) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008191552_Phase3A_BookingSafetyHardening'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_BookingSeats_TripSeatId] ON [BookingSeats] ([TripSeatId]) WHERE [IsActive] = 1');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008191552_Phase3A_BookingSafetyHardening'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_Bookings_HoldToken] ON [Bookings] ([HoldToken]) WHERE [HoldToken] IS NOT NULL');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008191552_Phase3A_BookingSafetyHardening'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261008191552_Phase3A_BookingSafetyHardening', N'8.0.20');
END;
GO

COMMIT;
GO

SET NOEXEC OFF;
GO


