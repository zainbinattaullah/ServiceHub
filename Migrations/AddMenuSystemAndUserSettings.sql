-- ============================================================
-- Migration: Menu System + User Settings
-- Run against your ServiceHub database in SSMS.
-- Safe to re-run (all inserts are guarded by IF NOT EXISTS).
-- ============================================================

SET NOCOUNT ON;

-- ============================================================
-- STEP 1 — Create tables (skip if already exist)
-- ============================================================

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'MenuItems')
BEGIN
    CREATE TABLE [dbo].[MenuItems] (
        [Id]          INT            IDENTITY(1,1) NOT NULL,
        [Name]        NVARCHAR(100)  NOT NULL,
        [Icon]        NVARCHAR(100)  NULL,
        [Url]         NVARCHAR(500)  NULL,
        [Area]        NVARCHAR(100)  NULL,
        [Controller]  NVARCHAR(100)  NULL,
        [Action]      NVARCHAR(100)  NULL,
        [ParentId]    INT            NULL,
        [OrderIndex]  INT            NOT NULL DEFAULT 0,
        [IsActive]    BIT            NOT NULL DEFAULT 1,
        CONSTRAINT [PK_MenuItems] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_MenuItems_Parent]
            FOREIGN KEY ([ParentId]) REFERENCES [dbo].[MenuItems]([Id])
            ON DELETE NO ACTION ON UPDATE NO ACTION
    );
    CREATE INDEX [IX_MenuItems_ParentId] ON [dbo].[MenuItems]([ParentId]);
    PRINT 'MenuItems table created.';
END
ELSE PRINT 'MenuItems already exists.';

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'RoleMenuItems')
BEGIN
    CREATE TABLE [dbo].[RoleMenuItems] (
        [Id]          INT           IDENTITY(1,1) NOT NULL,
        [RoleId]      NVARCHAR(450) NOT NULL,
        [MenuItemId]  INT           NOT NULL,
        CONSTRAINT [PK_RoleMenuItems] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_RoleMenuItems_MenuItem]
            FOREIGN KEY ([MenuItemId]) REFERENCES [dbo].[MenuItems]([Id])
            ON DELETE CASCADE,
        CONSTRAINT [UQ_RoleMenuItems_Role_Item] UNIQUE ([RoleId],[MenuItemId])
    );
    PRINT 'RoleMenuItems table created.';
END
ELSE PRINT 'RoleMenuItems already exists.';

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'UserSettings')
BEGIN
    CREATE TABLE [dbo].[UserSettings] (
        [Id]               INT           IDENTITY(1,1) NOT NULL,
        [UserId]           NVARCHAR(450) NOT NULL,
        [Theme]            NVARCHAR(50)  NOT NULL DEFAULT 'light',
        [SidebarCollapsed] BIT           NOT NULL DEFAULT 0,
        [UpdatedAt]        DATETIME2     NOT NULL DEFAULT GETDATE(),
        CONSTRAINT [PK_UserSettings] PRIMARY KEY ([Id]),
        CONSTRAINT [UQ_UserSettings_UserId] UNIQUE ([UserId])
    );
    PRINT 'UserSettings table created.';
END
ELSE PRINT 'UserSettings already exists.';

-- ============================================================
-- STEP 2 — Seed top-level items first (no parent dependency)
-- ============================================================

-- Dashboard (standalone)
IF NOT EXISTS (SELECT 1 FROM [dbo].[MenuItems] WHERE [Name] = 'Dashboard')
    INSERT INTO [dbo].[MenuItems] ([Name],[Icon],[Area],[Controller],[Action],[ParentId],[OrderIndex],[IsActive])
    VALUES ('Dashboard','bi bi-speedometer2',NULL,'Home','Index',NULL,10,1);

-- Machines (group header — Url='#', no controller)
IF NOT EXISTS (SELECT 1 FROM [dbo].[MenuItems] WHERE [Name] = 'Machines')
    INSERT INTO [dbo].[MenuItems] ([Name],[Icon],[Url],[Area],[Controller],[Action],[ParentId],[OrderIndex],[IsActive])
    VALUES ('Machines','bi bi-hdd-network','#',NULL,NULL,NULL,NULL,20,1);

-- Attendance (group header)
IF NOT EXISTS (SELECT 1 FROM [dbo].[MenuItems] WHERE [Name] = 'Attendance')
    INSERT INTO [dbo].[MenuItems] ([Name],[Icon],[Url],[Area],[Controller],[Action],[ParentId],[OrderIndex],[IsActive])
    VALUES ('Attendance','bi bi-calendar-check','#',NULL,NULL,NULL,NULL,30,1);

-- Employees (group header)
IF NOT EXISTS (SELECT 1 FROM [dbo].[MenuItems] WHERE [Name] = 'Employees')
    INSERT INTO [dbo].[MenuItems] ([Name],[Icon],[Url],[Area],[Controller],[Action],[ParentId],[OrderIndex],[IsActive])
    VALUES ('Employees','bi bi-people-fill','#',NULL,NULL,NULL,NULL,40,1);

-- Change Password (standalone)
IF NOT EXISTS (SELECT 1 FROM [dbo].[MenuItems] WHERE [Name] = 'Change Password')
    INSERT INTO [dbo].[MenuItems] ([Name],[Icon],[Url],[Area],[Controller],[Action],[ParentId],[OrderIndex],[IsActive])
    VALUES ('Change Password','bi bi-key-fill','/Identity/Account/Manage/ChangePassword',NULL,NULL,NULL,NULL,90,1);

-- ============================================================
-- STEP 3 — Seed children (read parent IDs after step 2)
-- ============================================================

-- Children of "Machines"
IF EXISTS (SELECT 1 FROM [dbo].[MenuItems] WHERE [Name] = 'Machines')
BEGIN
    DECLARE @MachinesId INT = (SELECT [Id] FROM [dbo].[MenuItems] WHERE [Name] = 'Machines');

    IF NOT EXISTS (SELECT 1 FROM [dbo].[MenuItems] WHERE [Name] = 'Attendance Machines')
        INSERT INTO [dbo].[MenuItems] ([Name],[Icon],[Area],[Controller],[Action],[ParentId],[OrderIndex],[IsActive])
        VALUES ('Attendance Machines','bi bi-fingerprint','HR','AttendanceMachine','Index',@MachinesId,10,1);

    IF NOT EXISTS (SELECT 1 FROM [dbo].[MenuItems] WHERE [Name] = 'Machine Logs')
        INSERT INTO [dbo].[MenuItems] ([Name],[Icon],[Area],[Controller],[Action],[ParentId],[OrderIndex],[IsActive])
        VALUES ('Machine Logs','bi bi-journal-text','HR','MachineLogs','Index',@MachinesId,20,1);

    IF NOT EXISTS (SELECT 1 FROM [dbo].[MenuItems] WHERE [Name] = 'Force Sync')
        INSERT INTO [dbo].[MenuItems] ([Name],[Icon],[Area],[Controller],[Action],[ParentId],[OrderIndex],[IsActive])
        VALUES ('Force Sync','bi bi-arrow-repeat','HR','AttendanceMachine','ForceSync',@MachinesId,30,1);

    IF NOT EXISTS (SELECT 1 FROM [dbo].[MenuItems] WHERE [Name] = 'Machine Lock')
        INSERT INTO [dbo].[MenuItems] ([Name],[Icon],[Area],[Controller],[Action],[ParentId],[OrderIndex],[IsActive])
        VALUES ('Machine Lock','bi bi-lock-fill','HR','AttendanceMachine','MachineLock',@MachinesId,40,1);
END

-- Children of "Attendance"
IF EXISTS (SELECT 1 FROM [dbo].[MenuItems] WHERE [Name] = 'Attendance')
BEGIN
    DECLARE @AttendanceId INT = (SELECT [Id] FROM [dbo].[MenuItems] WHERE [Name] = 'Attendance');

    IF NOT EXISTS (SELECT 1 FROM [dbo].[MenuItems] WHERE [Name] = 'Attendance Records')
        INSERT INTO [dbo].[MenuItems] ([Name],[Icon],[Area],[Controller],[Action],[ParentId],[OrderIndex],[IsActive])
        VALUES ('Attendance Records','bi bi-table','HR','Attendance','Index',@AttendanceId,10,1);
END

-- Children of "Employees"
IF EXISTS (SELECT 1 FROM [dbo].[MenuItems] WHERE [Name] = 'Employees')
BEGIN
    DECLARE @EmployeesId INT = (SELECT [Id] FROM [dbo].[MenuItems] WHERE [Name] = 'Employees');

    IF NOT EXISTS (SELECT 1 FROM [dbo].[MenuItems] WHERE [Name] = 'Employee List')
        INSERT INTO [dbo].[MenuItems] ([Name],[Icon],[Area],[Controller],[Action],[ParentId],[OrderIndex],[IsActive])
        VALUES ('Employee List','bi bi-person-lines-fill','HR','Employees','Index',@EmployeesId,10,1);

    IF NOT EXISTS (SELECT 1 FROM [dbo].[MenuItems] WHERE [Name] = 'Transfer Employee')
        INSERT INTO [dbo].[MenuItems] ([Name],[Icon],[Area],[Controller],[Action],[ParentId],[OrderIndex],[IsActive])
        VALUES ('Transfer Employee','bi bi-arrow-left-right','HR','Employees','Transfer',@EmployeesId,20,1);

    IF NOT EXISTS (SELECT 1 FROM [dbo].[MenuItems] WHERE [Name] = 'Employee Detail Report')
        INSERT INTO [dbo].[MenuItems] ([Name],[Icon],[Area],[Controller],[Action],[ParentId],[OrderIndex],[IsActive])
        VALUES ('Employee Detail Report','bi bi-person-vcard-fill','HR','EmployeeDetailReport','Index',@EmployeesId,30,1);
END

PRINT 'Menu items seeded.';

-- ============================================================
-- STEP 4 — Role assignments
-- Run AFTER starting the application at least once so that
-- Program.cs has created the Admin / HR / User roles.
-- Safe to re-run — duplicate inserts are blocked by UNIQUE constraint.
-- ============================================================

DECLARE @AdminId  NVARCHAR(450) = (SELECT TOP 1 [Id] FROM [dbo].[AspNetRoles] WHERE [NormalizedName] = 'ADMIN');
DECLARE @HrId     NVARCHAR(450) = (SELECT TOP 1 [Id] FROM [dbo].[AspNetRoles] WHERE [NormalizedName] = 'HR');
DECLARE @UserId   NVARCHAR(450) = (SELECT TOP 1 [Id] FROM [dbo].[AspNetRoles] WHERE [NormalizedName] = 'USER');

-- ── Items with NO restrictions (open to ALL authenticated users) ──────────
-- Dashboard, Change Password, and group headers (Url='#') are intentionally
-- left out of ALL role assignments. Zero RoleMenuItems rows = unrestricted.
-- The Admin bypass in the middleware also guarantees Admins are never blocked.

-- Admin → restricted functional items only (NOT open-access items above)
IF @AdminId IS NOT NULL
BEGIN
    INSERT INTO [dbo].[RoleMenuItems] ([RoleId],[MenuItemId])
    SELECT @AdminId, [Id]
    FROM   [dbo].[MenuItems]
    WHERE  [IsActive] = 1
      -- Exclude items that should be open to all
      AND  [Name] NOT IN ('Dashboard','Change Password','Machines','Attendance','Employees')
      AND  [Id] NOT IN (SELECT [MenuItemId] FROM [dbo].[RoleMenuItems] WHERE [RoleId] = @AdminId);
    PRINT 'Admin role assignments done.';
END
ELSE PRINT 'Admin role not found — re-run after first app launch.';

-- HR → Machines group + Attendance Machines + Machine Logs + all Attendance + all Employees
IF @HrId IS NOT NULL
BEGIN
    INSERT INTO [dbo].[RoleMenuItems] ([RoleId],[MenuItemId])
    SELECT @HrId, [Id]
    FROM   [dbo].[MenuItems]
    WHERE  [IsActive] = 1
      AND  [Name] IN (
               'Machines','Attendance Machines','Machine Logs',
               'Attendance','Attendance Records',
               'Employees','Employee List','Transfer Employee','Employee Detail Report',
               'Change Password'
           )
      AND  [Id] NOT IN (SELECT [MenuItemId] FROM [dbo].[RoleMenuItems] WHERE [RoleId] = @HrId);
    PRINT 'HR role assignments done.';
END
ELSE PRINT 'HR role not found — re-run after first app launch.';

-- User → Attendance group + Attendance Records + Employees group + Employee List
IF @UserId IS NOT NULL
BEGIN
    INSERT INTO [dbo].[RoleMenuItems] ([RoleId],[MenuItemId])
    SELECT @UserId, [Id]
    FROM   [dbo].[MenuItems]
    WHERE  [IsActive] = 1
      AND  [Name] IN (
               'Attendance','Attendance Records',
               'Employees','Employee List',
               'Change Password'
           )
      AND  [Id] NOT IN (SELECT [MenuItemId] FROM [dbo].[RoleMenuItems] WHERE [RoleId] = @UserId);
    PRINT 'User role assignments done.';
END
ELSE PRINT 'User role not found — re-run after first app launch.';

-- ============================================================
-- STEP 5 — Verify: show what was created
-- ============================================================
PRINT '';
PRINT '=== MenuItems ===';
SELECT
    m.[Id],
    ISNULL(p.[Name],'(top level)') AS [Parent],
    m.[Name],
    m.[OrderIndex],
    ISNULL(m.[Area]+'/','') + ISNULL(m.[Controller]+'/','') + ISNULL(m.[Action],'') AS [Route],
    (SELECT COUNT(*) FROM [dbo].[RoleMenuItems] r WHERE r.[MenuItemId] = m.[Id]) AS [RoleCount]
FROM      [dbo].[MenuItems] m
LEFT JOIN [dbo].[MenuItems] p ON p.[Id] = m.[ParentId]
ORDER BY  ISNULL(p.[OrderIndex], m.[OrderIndex]), m.[ParentId], m.[OrderIndex];

PRINT '=== Role Summary ===';
SELECT
    ar.[Name]             AS [Role],
    COUNT(rm.[MenuItemId]) AS [ItemsAssigned]
FROM      [dbo].[AspNetRoles]   ar
LEFT JOIN [dbo].[RoleMenuItems] rm ON rm.[RoleId] = ar.[Id]
GROUP BY  ar.[Name];
