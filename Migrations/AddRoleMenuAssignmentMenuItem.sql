-- ============================================================
-- Migration: Add "Role Menu Assignment" menu item
-- Run against your ServiceHub database in SSMS.
-- Safe to re-run (guarded by IF NOT EXISTS).
-- ============================================================

SET NOCOUNT ON;

-- Find the "Machines" or top-level group to place this under.
-- We add it as a child of whatever parent has Controller = 'MenuManagement',
-- or as a standalone top-level item if no such parent exists.

DECLARE @ParentId   INT = NULL;
DECLARE @NewId      INT;
DECLARE @MaxOrder   INT;

-- Try to find an existing MenuManagement parent group
SELECT @ParentId = ParentId
FROM   [dbo].[MenuItems]
WHERE  [Controller] = 'MenuManagement' AND [Action] = 'Index'
       AND ParentId IS NOT NULL;

-- Determine next OrderIndex under that parent (or at top level)
SELECT @MaxOrder = ISNULL(MAX(OrderIndex), 0) + 1
FROM   [dbo].[MenuItems]
WHERE  (ParentId = @ParentId OR (ParentId IS NULL AND @ParentId IS NULL));

-- Insert the new item only if it does not already exist
IF NOT EXISTS (
    SELECT 1 FROM [dbo].[MenuItems]
    WHERE [Area] = 'HR' AND [Controller] = 'MenuManagement' AND [Action] = 'RoleMenuAssignment'
)
BEGIN
    INSERT INTO [dbo].[MenuItems] ([Name], [Icon], [Url], [Area], [Controller], [Action], [ParentId], [OrderIndex], [IsActive])
    VALUES (
        'Role Menu Assignment',
        'fas fa-user-shield',
        NULL,
        'HR',
        'MenuManagement',
        'RoleMenuAssignment',
        @ParentId,
        @MaxOrder,
        1
    );

    SET @NewId = SCOPE_IDENTITY();
    PRINT 'Role Menu Assignment menu item inserted with Id = ' + CAST(@NewId AS NVARCHAR);

    -- Assign to Admin role only (same as all other MenuManagement items)
    DECLARE @AdminRoleId NVARCHAR(450);
    SELECT @AdminRoleId = [Id] FROM [dbo].[AspNetRoles] WHERE [NormalizedName] = 'ADMIN';

    IF @AdminRoleId IS NOT NULL AND NOT EXISTS (
        SELECT 1 FROM [dbo].[RoleMenuItems] WHERE [RoleId] = @AdminRoleId AND [MenuItemId] = @NewId
    )
    BEGIN
        INSERT INTO [dbo].[RoleMenuItems] ([RoleId], [MenuItemId]) VALUES (@AdminRoleId, @NewId);
        PRINT 'Admin role assigned to the new menu item.';
    END
END
ELSE
    PRINT 'Role Menu Assignment menu item already exists — skipped.';
