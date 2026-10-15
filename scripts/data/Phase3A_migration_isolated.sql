-- ============================================================================
-- TRIPGO DATABASE MIGRATION SCRIPT: Phase3A_PersistentBookingCore
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
    WHERE [MigrationId] = N'20261008183111_Phase3A_PersistentBookingCore'
)
BEGIN
    ALTER TABLE [Trips] ADD [IsDemoTrip] bit NOT NULL DEFAULT CAST(0 AS bit);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008183111_Phase3A_PersistentBookingCore'
)
BEGIN
    CREATE TABLE [Bookings] (
        [Id] bigint NOT NULL IDENTITY,
        [BookingCode] nvarchar(50) NOT NULL,
        [TripId] bigint NOT NULL,
        [PickupPointId] bigint NULL,
        [DropoffPointId] bigint NULL,
        [PickupPointNote] nvarchar(500) NOT NULL,
        [DropoffPointNote] nvarchar(500) NOT NULL,
        [UserId] bigint NULL,
        [PassengerName] nvarchar(200) NOT NULL,
        [PassengerPhone] nvarchar(50) NOT NULL,
        [PassengerEmail] nvarchar(200) NULL,
        [BaseAmount] decimal(18,2) NOT NULL,
        [SurchargeAmount] decimal(18,2) NOT NULL,
        [TotalAmount] decimal(18,2) NOT NULL,
        [HasInsurance] bit NOT NULL,
        [InsuranceAmount] decimal(18,2) NOT NULL,
        [Status] int NOT NULL,
        [PaymentStatus] int NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [ExpiresAt] datetimeoffset NOT NULL,
        [ConfirmedAt] datetimeoffset NULL,
        [CancelledAt] datetimeoffset NULL,
        [CancellationReason] nvarchar(500) NULL,
        [IsDemoBooking] bit NOT NULL,
        CONSTRAINT [PK_Bookings] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Bookings_PickupDropoffPoints_DropoffPointId] FOREIGN KEY ([DropoffPointId]) REFERENCES [PickupDropoffPoints] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Bookings_PickupDropoffPoints_PickupPointId] FOREIGN KEY ([PickupPointId]) REFERENCES [PickupDropoffPoints] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Bookings_Trips_TripId] FOREIGN KEY ([TripId]) REFERENCES [Trips] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Bookings_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008183111_Phase3A_PersistentBookingCore'
)
BEGIN
    CREATE TABLE [TripSeats] (
        [Id] bigint NOT NULL IDENTITY,
        [TripId] bigint NOT NULL,
        [SeatLayoutItemId] bigint NULL,
        [SeatCode] nvarchar(20) NOT NULL,
        [SeatType] nvarchar(100) NOT NULL,
        [Floor] int NOT NULL,
        [Row] int NOT NULL,
        [Column] int NOT NULL,
        [Price] decimal(18,2) NOT NULL,
        [Status] int NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [UpdatedAt] datetimeoffset NULL,
        CONSTRAINT [PK_TripSeats] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_TripSeats_SeatLayoutItems_SeatLayoutItemId] FOREIGN KEY ([SeatLayoutItemId]) REFERENCES [SeatLayoutItems] ([Id]) ON DELETE SET NULL,
        CONSTRAINT [FK_TripSeats_Trips_TripId] FOREIGN KEY ([TripId]) REFERENCES [Trips] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008183111_Phase3A_PersistentBookingCore'
)
BEGIN
    CREATE TABLE [BookingPassengers] (
        [Id] bigint NOT NULL IDENTITY,
        [BookingId] bigint NOT NULL,
        [FullName] nvarchar(200) NOT NULL,
        [PhoneNumber] nvarchar(50) NOT NULL,
        [Email] nvarchar(200) NULL,
        [IdentityCard] nvarchar(50) NULL,
        [IsPrimaryContact] bit NOT NULL,
        CONSTRAINT [PK_BookingPassengers] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_BookingPassengers_Bookings_BookingId] FOREIGN KEY ([BookingId]) REFERENCES [Bookings] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008183111_Phase3A_PersistentBookingCore'
)
BEGIN
    CREATE TABLE [BookingSeats] (
        [Id] bigint NOT NULL IDENTITY,
        [BookingId] bigint NOT NULL,
        [TripSeatId] bigint NOT NULL,
        [SeatCode] nvarchar(20) NOT NULL,
        [Price] decimal(18,2) NOT NULL,
        CONSTRAINT [PK_BookingSeats] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_BookingSeats_Bookings_BookingId] FOREIGN KEY ([BookingId]) REFERENCES [Bookings] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_BookingSeats_TripSeats_TripSeatId] FOREIGN KEY ([TripSeatId]) REFERENCES [TripSeats] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008183111_Phase3A_PersistentBookingCore'
)
BEGIN
    CREATE TABLE [SeatHolds] (
        [Id] bigint NOT NULL IDENTITY,
        [TripSeatId] bigint NOT NULL,
        [HoldToken] nvarchar(100) NOT NULL,
        [HeldAt] datetimeoffset NOT NULL,
        [ExpiresAt] datetimeoffset NOT NULL,
        [Status] int NOT NULL,
        CONSTRAINT [PK_SeatHolds] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_SeatHolds_TripSeats_TripSeatId] FOREIGN KEY ([TripSeatId]) REFERENCES [TripSeats] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008183111_Phase3A_PersistentBookingCore'
)
BEGIN
    CREATE INDEX [IX_BookingPassengers_BookingId] ON [BookingPassengers] ([BookingId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008183111_Phase3A_PersistentBookingCore'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Bookings_BookingCode] ON [Bookings] ([BookingCode]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008183111_Phase3A_PersistentBookingCore'
)
BEGIN
    CREATE INDEX [IX_Bookings_DropoffPointId] ON [Bookings] ([DropoffPointId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008183111_Phase3A_PersistentBookingCore'
)
BEGIN
    CREATE INDEX [IX_Bookings_PassengerPhone_CreatedAt] ON [Bookings] ([PassengerPhone], [CreatedAt]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008183111_Phase3A_PersistentBookingCore'
)
BEGIN
    CREATE INDEX [IX_Bookings_PickupPointId] ON [Bookings] ([PickupPointId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008183111_Phase3A_PersistentBookingCore'
)
BEGIN
    CREATE INDEX [IX_Bookings_TripId_Status] ON [Bookings] ([TripId], [Status]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008183111_Phase3A_PersistentBookingCore'
)
BEGIN
    CREATE INDEX [IX_Bookings_UserId] ON [Bookings] ([UserId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008183111_Phase3A_PersistentBookingCore'
)
BEGIN
    CREATE UNIQUE INDEX [IX_BookingSeats_BookingId_TripSeatId] ON [BookingSeats] ([BookingId], [TripSeatId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008183111_Phase3A_PersistentBookingCore'
)
BEGIN
    CREATE INDEX [IX_BookingSeats_TripSeatId] ON [BookingSeats] ([TripSeatId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008183111_Phase3A_PersistentBookingCore'
)
BEGIN
    CREATE INDEX [IX_SeatHolds_HoldToken_Status] ON [SeatHolds] ([HoldToken], [Status]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008183111_Phase3A_PersistentBookingCore'
)
BEGIN
    CREATE INDEX [IX_SeatHolds_TripSeatId_Status_ExpiresAt] ON [SeatHolds] ([TripSeatId], [Status], [ExpiresAt]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008183111_Phase3A_PersistentBookingCore'
)
BEGIN
    CREATE INDEX [IX_TripSeats_SeatLayoutItemId] ON [TripSeats] ([SeatLayoutItemId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008183111_Phase3A_PersistentBookingCore'
)
BEGIN
    CREATE UNIQUE INDEX [IX_TripSeats_TripId_SeatCode] ON [TripSeats] ([TripId], [SeatCode]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008183111_Phase3A_PersistentBookingCore'
)
BEGIN
    CREATE INDEX [IX_TripSeats_TripId_Status] ON [TripSeats] ([TripId], [Status]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008183111_Phase3A_PersistentBookingCore'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261008183111_Phase3A_PersistentBookingCore', N'8.0.20');
END;
GO

COMMIT;
GO

SET NOEXEC OFF;
GO


