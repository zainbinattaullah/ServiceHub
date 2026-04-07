-- ============================================================
-- Migration: Add Employee Password Update + Employee Registration
--            under Employees menu, and assign all roles.
-- Run against your ServiceHub database in SSMS.
-- Safe to re-run (all inserts are guarded by IF NOT EXISTS).
-- ============================================================

SET NOCOUNT ON;

-- ============================================================
-- STEP 1 — Move "Change Password" under Employees group
--          and rename to "Employee Password Update"
-- ============================================================

-- Get the Employees parent group ID
DECLARE @EmployeesId INT = (SELECT TOP 1 [Id] FROM [dbo].[MenuItems] WHERE [Name] = 'Employees' AND [ParentId] IS NULL);

-- If standalone "Change Password" exists at top level, move it under Employees and rename
IF EXISTS (SELECT 1 FROM [dbo].[MenuItems] WHERE [Name] = 'Change Password' AND [ParentId] IS NULL)
BEGIN
    UPDATE [dbo].[MenuItems]
    SET    [Name]       = 'Employee Password Update',
           [Icon]       = 'fas fa-key',
           [ParentId]   = @EmployeesId,
           [OrderIndex] = 40,
           [Area]       = 'HR',
           [Controller] = 'PasswordChange',
           [Action]     = 'Index',
           [Url]        = NULL
    WHERE  [Name] = 'Change Password' AND [ParentId] IS NULL;
    PRINT 'Moved "Change Password" -> "Employee Password Update" under Employees.';
END
ELSE IF NOT EXISTS (SELECT 1 FROM [dbo].[MenuItems] WHERE [Name] = 'Employee Password Update')
BEGIN
    -- Insert fresh if it doesn't exist at all
    INSERT INTO [dbo].[MenuItems] ([Name],[Icon],[Area],[Controller],[Action],[ParentId],[OrderIndex],[IsActive])
    VALUES ('Employee Password Update','fas fa-key','HR','PasswordChange','Index',@EmployeesId,40,1);
    PRINT 'Inserted "Employee Password Update" under Employees.';
END
ELSE PRINT '"Employee Password Update" already exists.';


-- ============================================================
-- STEP 2 — Add "Employee Registration" under Employees group
-- ============================================================

IF NOT EXISTS (SELECT 1 FROM [dbo].[MenuItems] WHERE [Name] = 'Employee Registration')
BEGIN
    INSERT INTO [dbo].[MenuItems] ([Name],[Icon],[Area],[Controller],[Action],[ParentId],[OrderIndex],[IsActive])
    VALUES ('Employee Registration','fas fa-user-plus','HR','EmployeeRegistration','Register',@EmployeesId,50,1);
    PRINT 'Inserted "Employee Registration" under Employees.';
END
ELSE PRINT '"Employee Registration" already exists.';


-- ============================================================
-- STEP 3 — Role Assignments (Admin = ALL, HR, User)
-- ============================================================

DECLARE @AdminId  NVARCHAR(450) = (SELECT TOP 1 [Id] FROM [dbo].[AspNetRoles] WHERE [NormalizedName] = 'ADMIN');
DECLARE @HrId     NVARCHAR(450) = (SELECT TOP 1 [Id] FROM [dbo].[AspNetRoles] WHERE [NormalizedName] = 'HR');
DECLARE @UserId   NVARCHAR(450) = (SELECT TOP 1 [Id] FROM [dbo].[AspNetRoles] WHERE [NormalizedName] = 'USER');

-- Get IDs of the two new/updated menu items
DECLARE @EmpPwdId INT  = (SELECT TOP 1 [Id] FROM [dbo].[MenuItems] WHERE [Name] = 'Employee Password Update');
DECLARE @EmpRegId INT  = (SELECT TOP 1 [Id] FROM [dbo].[MenuItems] WHERE [Name] = 'Employee Registration');

-- ── Admin → gets BOTH new items ──────────────────────────────
IF @AdminId IS NOT NULL
BEGIN
    IF @EmpPwdId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM [dbo].[RoleMenuItems] WHERE [RoleId] = @AdminId AND [MenuItemId] = @EmpPwdId)
        INSERT INTO [dbo].[RoleMenuItems] ([RoleId],[MenuItemId]) VALUES (@AdminId, @EmpPwdId);

    IF @EmpRegId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM [dbo].[RoleMenuItems] WHERE [RoleId] = @AdminId AND [MenuItemId] = @EmpRegId)
        INSERT INTO [dbo].[RoleMenuItems] ([RoleId],[MenuItemId]) VALUES (@AdminId, @EmpRegId);

    -- Ensure Admin has ALL existing menu items assigned too
    INSERT INTO [dbo].[RoleMenuItems] ([RoleId],[MenuItemId])
    SELECT @AdminId, m.[Id]
    FROM   [dbo].[MenuItems] m
    WHERE  m.[IsActive] = 1
      AND  m.[Id] NOT IN (SELECT [MenuItemId] FROM [dbo].[RoleMenuItems] WHERE [RoleId] = @AdminId);

    PRINT 'Admin role: all items assigned.';
END

-- ── HR → gets BOTH new items ─────────────────────────────────
IF @HrId IS NOT NULL
BEGIN
    IF @EmpPwdId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM [dbo].[RoleMenuItems] WHERE [RoleId] = @HrId AND [MenuItemId] = @EmpPwdId)
        INSERT INTO [dbo].[RoleMenuItems] ([RoleId],[MenuItemId]) VALUES (@HrId, @EmpPwdId);

    IF @EmpRegId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM [dbo].[RoleMenuItems] WHERE [RoleId] = @HrId AND [MenuItemId] = @EmpRegId)
        INSERT INTO [dbo].[RoleMenuItems] ([RoleId],[MenuItemId]) VALUES (@HrId, @EmpRegId);

    PRINT 'HR role: Employee Password Update + Employee Registration assigned.';
END

-- ── User → gets Employee Password Update only (NOT registration) ──
IF @UserId IS NOT NULL
BEGIN
    IF @EmpPwdId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM [dbo].[RoleMenuItems] WHERE [RoleId] = @UserId AND [MenuItemId] = @EmpPwdId)
        INSERT INTO [dbo].[RoleMenuItems] ([RoleId],[MenuItemId]) VALUES (@UserId, @EmpPwdId);

    PRINT 'User role: Employee Password Update assigned.';
END


-- ============================================================
-- STEP 4 — Verify: show current menu tree with role counts
-- ============================================================

PRINT '';
PRINT '=== Full Menu Tree ===';
SELECT
    m.[Id],
    ISNULL(p.[Name], '(top level)') AS [Parent],
    m.[Name],
    m.[OrderIndex],
    CASE
        WHEN m.[Url] IS NOT NULL THEN m.[Url]
        ELSE ISNULL(m.[Area] + '/', '') + ISNULL(m.[Controller] + '/', '') + ISNULL(m.[Action], '')
    END AS [Route],
    m.[IsActive],
    (SELECT COUNT(*) FROM [dbo].[RoleMenuItems] r WHERE r.[MenuItemId] = m.[Id]) AS [RoleCount]
FROM      [dbo].[MenuItems] m
LEFT JOIN [dbo].[MenuItems] p ON p.[Id] = m.[ParentId]
ORDER BY  ISNULL(m.[ParentId], m.[Id]), m.[ParentId], m.[OrderIndex];

PRINT '';
PRINT '=== Role Assignment Detail ===';
SELECT
    ar.[Name]  AS [Role],
    mi.[Name]  AS [MenuItem],
    mi.[Id]    AS [MenuItemId]
FROM      [dbo].[RoleMenuItems] rm
JOIN      [dbo].[AspNetRoles]   ar ON ar.[Id] = rm.[RoleId]
JOIN      [dbo].[MenuItems]     mi ON mi.[Id] = rm.[MenuItemId]
ORDER BY  ar.[Name], mi.[OrderIndex];

PRINT '';
PRINT '=== Role Summary ===';
SELECT
    ar.[Name]              AS [Role],
    COUNT(rm.[MenuItemId]) AS [TotalItemsAssigned]
FROM      [dbo].[AspNetRoles]   ar
LEFT JOIN [dbo].[RoleMenuItems] rm ON rm.[RoleId] = ar.[Id]
GROUP BY  ar.[Name];
