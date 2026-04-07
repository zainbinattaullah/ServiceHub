-- ============================================================
-- IMMEDIATE FIX — Access Denied on every page
-- Run this in SSMS, then restart the application.
-- ============================================================

SET NOCOUNT ON;

-- ── STEP 1 ──────────────────────────────────────────────────
-- Remove role restrictions from items that should be visible
-- to ALL authenticated users (Dashboard, Change Password,
-- and all group header rows which are URL='#').
-- Items with ZERO RoleMenuItems rows = open to everyone.
-- ────────────────────────────────────────────────────────────
DELETE FROM [dbo].[RoleMenuItems]
WHERE [MenuItemId] IN (
    SELECT [Id] FROM [dbo].[MenuItems]
    WHERE [Name] IN (
        'Dashboard',
        'Change Password',
        'Machines',       -- group header, no real route
        'Attendance',     -- group header
        'Employees'       -- group header
    )
);
PRINT 'Step 1 done — removed restrictions from open-access items.';

-- ── STEP 2 ──────────────────────────────────────────────────
-- Assign your user account to the Admin role.
-- !! Replace the email below with your actual login email !!
-- ────────────────────────────────────────────────────────────
DECLARE @MyEmail NVARCHAR(256) = 'admin@example.com'; -- ← CHANGE THIS

DECLARE @MyUserId   NVARCHAR(450) = (SELECT [Id] FROM [dbo].[AspNetUsers]  WHERE [NormalizedUserName] = UPPER(@MyEmail));
DECLARE @AdminRoleId2 NVARCHAR(450) = (SELECT [Id] FROM [dbo].[AspNetRoles] WHERE [NormalizedName]    = 'ADMIN');

IF @MyUserId IS NULL
    PRINT 'User not found — check the email address above.';
ELSE IF @AdminRoleId2 IS NULL
    PRINT 'Admin role not found — start the application once first, then re-run.';
ELSE
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM [dbo].[AspNetUserRoles]
        WHERE [UserId] = @MyUserId AND [RoleId] = @AdminRoleId2
    )
    BEGIN
        INSERT INTO [dbo].[AspNetUserRoles] ([UserId],[RoleId])
        VALUES (@MyUserId, @AdminRoleId2);
        PRINT 'Admin role assigned to ' + @MyEmail;
    END
    ELSE
        PRINT @MyEmail + ' already has the Admin role.';
END

-- ── Verify ───────────────────────────────────────────────────
PRINT '';
PRINT '=== Current role assignments for your user ===';
SELECT u.[UserName], r.[Name] AS [Role]
FROM   [dbo].[AspNetUsers]     u
JOIN   [dbo].[AspNetUserRoles] ur ON ur.[UserId] = u.[Id]
JOIN   [dbo].[AspNetRoles]     r  ON r.[Id]      = ur.[RoleId]
WHERE  u.[NormalizedUserName] = UPPER(@MyEmail);

PRINT '';
PRINT '=== MenuItems with NO role restrictions (open to all) ===';
SELECT [Name], [Controller], [Action]
FROM   [dbo].[MenuItems]
WHERE  [IsActive] = 1
  AND  [Id] NOT IN (SELECT [MenuItemId] FROM [dbo].[RoleMenuItems])
ORDER BY [OrderIndex];
