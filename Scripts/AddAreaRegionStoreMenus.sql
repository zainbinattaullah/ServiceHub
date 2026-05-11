-- ============================================================
-- Add Area, Region, Store tables and menu items
-- Run this script ONCE on the ServiceHub database
-- ============================================================

-- ── 1. Create Areas table ────────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'Areas')
BEGIN
    CREATE TABLE Areas (
        Id          INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        Code        NVARCHAR(20)  NOT NULL,
        Name        NVARCHAR(100) NOT NULL,
        Description NVARCHAR(255) NULL,
        IsActive    BIT           NOT NULL DEFAULT 1,
        CreatedAt   DATETIME2     NOT NULL DEFAULT GETDATE(),
        UpdatedAt   DATETIME2     NULL
    );
    PRINT 'Areas table created.';
END
ELSE
    PRINT 'Areas table already exists — skipped.';

-- ── 2. Create Regions table ──────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'Regions')
BEGIN
    CREATE TABLE Regions (
        Id          INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        Code        NVARCHAR(20)  NOT NULL,
        Name        NVARCHAR(100) NOT NULL,
        Description NVARCHAR(255) NULL,
        IsActive    BIT           NOT NULL DEFAULT 1,
        CreatedAt   DATETIME2     NOT NULL DEFAULT GETDATE(),
        UpdatedAt   DATETIME2     NULL
    );
    PRINT 'Regions table created.';
END
ELSE
    PRINT 'Regions table already exists — skipped.';

-- ── 3. Alter Stores table: add AreaId, RegionId, IsActive, timestamps ─
IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Stores' AND COLUMN_NAME = 'AreaId')
BEGIN
    ALTER TABLE Stores ADD AreaId INT NULL;
    PRINT 'Added AreaId to Stores.';
END

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Stores' AND COLUMN_NAME = 'RegionId')
BEGIN
    ALTER TABLE Stores ADD RegionId INT NULL;
    PRINT 'Added RegionId to Stores.';
END

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Stores' AND COLUMN_NAME = 'IsActive')
BEGIN
    ALTER TABLE Stores ADD IsActive BIT NOT NULL DEFAULT 1;
    PRINT 'Added IsActive to Stores.';
END

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Stores' AND COLUMN_NAME = 'CreatedAt')
BEGIN
    ALTER TABLE Stores ADD CreatedAt DATETIME2 NOT NULL DEFAULT GETDATE();
    PRINT 'Added CreatedAt to Stores.';
END

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Stores' AND COLUMN_NAME = 'UpdatedAt')
BEGIN
    ALTER TABLE Stores ADD UpdatedAt DATETIME2 NULL;
    PRINT 'Added UpdatedAt to Stores.';
END

-- FK from Stores.AreaId → Areas.Id
IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS WHERE CONSTRAINT_NAME = 'FK_Stores_Areas')
BEGIN
    ALTER TABLE Stores
        ADD CONSTRAINT FK_Stores_Areas
        FOREIGN KEY (AreaId) REFERENCES Areas(Id) ON DELETE SET NULL;
    PRINT 'FK_Stores_Areas created.';
END

-- FK from Stores.RegionId → Regions.Id
IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS WHERE CONSTRAINT_NAME = 'FK_Stores_Regions')
BEGIN
    ALTER TABLE Stores
        ADD CONSTRAINT FK_Stores_Regions
        FOREIGN KEY (RegionId) REFERENCES Regions(Id) ON DELETE SET NULL;
    PRINT 'FK_Stores_Regions created.';
END

-- ── 4. Alter AttendenceMachines: add StoreId ─────────────────
IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'AttendenceMachines' AND COLUMN_NAME = 'StoreId')
BEGIN
    ALTER TABLE AttendenceMachines ADD StoreId INT NULL;
    PRINT 'Added StoreId to AttendenceMachines.';
END

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS WHERE CONSTRAINT_NAME = 'FK_AttendenceMachines_Stores')
BEGIN
    ALTER TABLE AttendenceMachines
        ADD CONSTRAINT FK_AttendenceMachines_Stores
        FOREIGN KEY (StoreId) REFERENCES Stores(Id) ON DELETE SET NULL;
    PRINT 'FK_AttendenceMachines_Stores created.';
END

-- ── 5. Insert menu items ──────────────────────────────────────
-- Find or create the HR parent menu group
DECLARE @hrParentId INT;
SELECT @hrParentId = Id FROM MenuItems WHERE Controller IS NULL AND Name = 'HR' AND ParentId IS NULL;

-- If no HR parent group exists, create one
IF @hrParentId IS NULL
BEGIN
    INSERT INTO MenuItems (Name, Icon, Url, Area, Controller, Action, ParentId, OrderIndex, IsActive)
    VALUES ('HR', 'fas fa-users-cog', NULL, NULL, NULL, NULL, NULL, 20, 1);
    SET @hrParentId = SCOPE_IDENTITY();
    PRINT 'Created HR parent menu group.';
END

-- Determine next OrderIndex for children of HR group
DECLARE @nextOrder INT;
SELECT @nextOrder = ISNULL(MAX(OrderIndex), 0) + 10 FROM MenuItems WHERE ParentId = @hrParentId;

-- Area List menu item
IF NOT EXISTS (SELECT 1 FROM MenuItems WHERE Area = 'HR' AND Controller = 'Area' AND Action = 'Index')
BEGIN
    INSERT INTO MenuItems (Name, Icon, Url, Area, Controller, Action, ParentId, OrderIndex, IsActive)
    VALUES ('Area List', 'fas fa-map-marker-alt', NULL, 'HR', 'Area', 'Index', @hrParentId, @nextOrder, 1);
    SET @nextOrder = @nextOrder + 10;
    PRINT 'Area List menu item inserted.';
END
ELSE
    PRINT 'Area List menu item already exists — skipped.';

-- Region List menu item
IF NOT EXISTS (SELECT 1 FROM MenuItems WHERE Area = 'HR' AND Controller = 'Region' AND Action = 'Index')
BEGIN
    INSERT INTO MenuItems (Name, Icon, Url, Area, Controller, Action, ParentId, OrderIndex, IsActive)
    VALUES ('Region List', 'fas fa-globe', NULL, 'HR', 'Region', 'Index', @hrParentId, @nextOrder, 1);
    SET @nextOrder = @nextOrder + 10;
    PRINT 'Region List menu item inserted.';
END
ELSE
    PRINT 'Region List menu item already exists — skipped.';

-- Store List menu item
IF NOT EXISTS (SELECT 1 FROM MenuItems WHERE Area = 'HR' AND Controller = 'Store' AND Action = 'Index')
BEGIN
    INSERT INTO MenuItems (Name, Icon, Url, Area, Controller, Action, ParentId, OrderIndex, IsActive)
    VALUES ('Store List', 'fas fa-store', NULL, 'HR', 'Store', 'Index', @hrParentId, @nextOrder, 1);
    PRINT 'Store List menu item inserted.';
END
ELSE
    PRINT 'Store List menu item already exists — skipped.';

-- ── 6. Assign new menu items to Admin role ────────────────────
DECLARE @adminRoleId NVARCHAR(450);
SELECT @adminRoleId = Id FROM AspNetRoles WHERE NormalizedName = 'ADMIN';

IF @adminRoleId IS NULL
BEGIN
    PRINT 'WARNING: Admin role not found — skipping RoleMenuItems insert.';
END
ELSE
BEGIN
    -- Area List
    DECLARE @areaMenuId INT;
    SELECT @areaMenuId = Id FROM MenuItems WHERE Area = 'HR' AND Controller = 'Area' AND Action = 'Index';

    IF @areaMenuId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM RoleMenuItems WHERE RoleId = @adminRoleId AND MenuItemId = @areaMenuId)
    BEGIN
        INSERT INTO RoleMenuItems (RoleId, MenuItemId) VALUES (@adminRoleId, @areaMenuId);
        PRINT 'Assigned Area List to Admin role.';
    END

    -- Region List
    DECLARE @regionMenuId INT;
    SELECT @regionMenuId = Id FROM MenuItems WHERE Area = 'HR' AND Controller = 'Region' AND Action = 'Index';

    IF @regionMenuId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM RoleMenuItems WHERE RoleId = @adminRoleId AND MenuItemId = @regionMenuId)
    BEGIN
        INSERT INTO RoleMenuItems (RoleId, MenuItemId) VALUES (@adminRoleId, @regionMenuId);
        PRINT 'Assigned Region List to Admin role.';
    END

    -- Store List
    DECLARE @storeMenuId INT;
    SELECT @storeMenuId = Id FROM MenuItems WHERE Area = 'HR' AND Controller = 'Store' AND Action = 'Index';

    IF @storeMenuId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM RoleMenuItems WHERE RoleId = @adminRoleId AND MenuItemId = @storeMenuId)
    BEGIN
        INSERT INTO RoleMenuItems (RoleId, MenuItemId) VALUES (@adminRoleId, @storeMenuId);
        PRINT 'Assigned Store List to Admin role.';
    END
END

PRINT 'Migration complete.';
