SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008192859_Phase3A_HoldOwnershipReleaseGate'
)
BEGIN
    ALTER TABLE [SeatHolds] ADD [OwnerIdentifier] nvarchar(150) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008192859_Phase3A_HoldOwnershipReleaseGate'
)
BEGIN
    CREATE INDEX [IX_SeatHolds_HoldToken_OwnerIdentifier] ON [SeatHolds] ([HoldToken], [OwnerIdentifier]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008192859_Phase3A_HoldOwnershipReleaseGate'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261008192859_Phase3A_HoldOwnershipReleaseGate', N'8.0.20');
END;
GO

COMMIT;
GO

