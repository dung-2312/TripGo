-- ============================================================================
-- TRIPGO DATABASE MIGRATION SCRIPT: Phase3B_PaymentTransactions
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

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015092939_Phase3B_PaymentTransactions'
)
BEGIN
    CREATE TABLE [PaymentTransactions] (
        [Id] bigint NOT NULL IDENTITY,
        [BookingId] bigint NOT NULL,
        [TransactionCode] nvarchar(100) NOT NULL,
        [PaymentMethod] nvarchar(50) NOT NULL,
        [Amount] decimal(18,2) NOT NULL,
        [Status] int NOT NULL,
        [ProviderReference] nvarchar(100) NULL,
        [Note] nvarchar(500) NULL,
        [IsSandbox] bit NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [CompletedAt] datetimeoffset NULL,
        CONSTRAINT [PK_PaymentTransactions] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_PaymentTransactions_Bookings_BookingId] FOREIGN KEY ([BookingId]) REFERENCES [Bookings] ([Id]) ON DELETE CASCADE
    );

    CREATE INDEX [IX_PaymentTransactions_BookingId_Status] ON [PaymentTransactions] ([BookingId], [Status]);

    CREATE UNIQUE INDEX [IX_PaymentTransactions_TransactionCode] ON [PaymentTransactions] ([TransactionCode]);

    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261015092939_Phase3B_PaymentTransactions', N'8.0.20');
END;
GO

COMMIT TRANSACTION;
GO
