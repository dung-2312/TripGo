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

