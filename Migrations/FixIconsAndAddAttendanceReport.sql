-- ============================================================
-- 1. Fix Employee Detail Report icon  (fas fa-user-chart is FA Pro)
--    Replace with a free equivalent.
-- ============================================================
UPDATE [dbo].[MenuItems]
SET    [Icon] = 'fas fa-chart-bar'
WHERE  [Name] = 'Employee Detail Report';

-- ============================================================
-- 2. Add "Attendance Report" as a child of the Attendance group
-- ============================================================
IF EXISTS (SELECT 1 FROM [dbo].[MenuItems] WHERE [Name] = 'Attendance' AND [ParentId] IS NULL)
BEGIN
    DECLARE @AttId INT = (SELECT [Id] FROM [dbo].[MenuItems]
                          WHERE  [Name] = 'Attendance' AND [ParentId] IS NULL);

    IF NOT EXISTS (SELECT 1 FROM [dbo].[MenuItems] WHERE [Name] = 'Attendance Report')
    BEGIN
        INSERT INTO [dbo].[MenuItems]
            ([Name],[Icon],[Area],[Controller],[Action],[ParentId],[OrderIndex],[IsActive])
        VALUES
            ('Attendance Report','fas fa-file-alt','HR','AttendanceReport','Index',@AttId,20,1);

        PRINT 'Attendance Report menu item added.';
    END
    ELSE
    BEGIN
        -- If it exists, update its controller/action in case they changed
        UPDATE [dbo].[MenuItems]
        SET    [Icon]       = 'fas fa-file-alt',
               [Area]       = 'HR',
               [Controller] = 'AttendanceReport',
               [Action]     = 'Index',
               [ParentId]   = @AttId,
               [OrderIndex] = 20,
               [IsActive]   = 1
        WHERE  [Name] = 'Attendance Report';

        PRINT 'Attendance Report menu item updated.';
    END

    -- Assign same roles as "Attendance Records" has
    DECLARE @NewItemId  INT           = (SELECT [Id] FROM [dbo].[MenuItems] WHERE [Name] = 'Attendance Report');
    DECLARE @AdminRoleId NVARCHAR(450) = (SELECT [Id] FROM [dbo].[AspNetRoles] WHERE [NormalizedName] = 'ADMIN');
    DECLARE @HrRoleId    NVARCHAR(450) = (SELECT [Id] FROM [dbo].[AspNetRoles] WHERE [NormalizedName] = 'HR');

    IF @AdminRoleId IS NOT NULL AND NOT EXISTS (
        SELECT 1 FROM [dbo].[RoleMenuItems] WHERE [RoleId] = @AdminRoleId AND [MenuItemId] = @NewItemId)
        INSERT INTO [dbo].[RoleMenuItems] ([RoleId],[MenuItemId]) VALUES (@AdminRoleId, @NewItemId);

    IF @HrRoleId IS NOT NULL AND NOT EXISTS (
        SELECT 1 FROM [dbo].[RoleMenuItems] WHERE [RoleId] = @HrRoleId AND [MenuItemId] = @NewItemId)
        INSERT INTO [dbo].[RoleMenuItems] ([RoleId],[MenuItemId]) VALUES (@HrRoleId, @NewItemId);

    PRINT 'Role assignments done for Attendance Report.';
END
ELSE
    PRINT 'Attendance group not found — run AddMenuSystemAndUserSettings.sql first.';

-- ============================================================
-- Verify
-- ============================================================
SELECT m.[Name], m.[Icon], m.[Controller], m.[Action],
       p.[Name] AS [Parent],
       (SELECT COUNT(*) FROM [dbo].[RoleMenuItems] r WHERE r.[MenuItemId] = m.[Id]) AS [Roles]
FROM   [dbo].[MenuItems] m
LEFT JOIN [dbo].[MenuItems] p ON p.[Id] = m.[ParentId]
WHERE  m.[Name] IN ('Employee Detail Report','Attendance Report','Attendance Records')
ORDER BY m.[ParentId], m.[OrderIndex];
