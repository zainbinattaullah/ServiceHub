-- ============================================================
-- Migration: EmployeeDeviceCommands table
-- Queues Activate/Deactivate commands for the Windows Service.
-- Safe to re-run (guarded by IF NOT EXISTS).
-- ============================================================
SET NOCOUNT ON;

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'EmployeeDeviceCommands')
BEGIN
    CREATE TABLE [dbo].[EmployeeDeviceCommands] (
        [Id]                  INT            IDENTITY(1,1) NOT NULL,
        [EmployeeCode]        NVARCHAR(50)   NOT NULL,
        [EmployeeName]        NVARCHAR(250)  NULL,
        [Action]              NVARCHAR(20)   NOT NULL,  -- 'Activate' | 'Deactivate'
        [Status]              NVARCHAR(20)   NOT NULL DEFAULT 'Pending',
        [RequestedAt]         DATETIME       NOT NULL  DEFAULT GETDATE(),
        [RequestedByUserId]   NVARCHAR(450)  NULL,
        [RequestedByUserName] NVARCHAR(450)  NULL,
        [ProcessedAt]         DATETIME       NULL,
        [MachinesAttempted]   INT            NULL,
        [MachinesSucceeded]   INT            NULL,
        [ErrorMessage]        NVARCHAR(MAX)  NULL,
        CONSTRAINT [PK_EmployeeDeviceCommands] PRIMARY KEY ([Id])
    );
    CREATE INDEX [IX_EmployeeDeviceCommands_Status]       ON [dbo].[EmployeeDeviceCommands]([Status]);
    CREATE INDEX [IX_EmployeeDeviceCommands_EmployeeCode] ON [dbo].[EmployeeDeviceCommands]([EmployeeCode]);
    PRINT 'EmployeeDeviceCommands table created.';
END
ELSE
    PRINT 'EmployeeDeviceCommands already exists — skipped.';

-- Also add IsActive column to EmployeeEnrollments if missing
IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
    WHERE TABLE_NAME = 'EmployeeEnrollments' AND COLUMN_NAME = 'IsActive'
)
BEGIN
    ALTER TABLE [dbo].[EmployeeEnrollments] ADD [IsActive] BIT NOT NULL DEFAULT 1;
    PRINT 'IsActive added to EmployeeEnrollments.';
END
ELSE
    PRINT 'EmployeeEnrollments.IsActive already exists.';
