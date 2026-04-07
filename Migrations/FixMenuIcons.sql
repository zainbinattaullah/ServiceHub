-- ============================================================
-- Fix menu icons: replace bi bi-* with Font Awesome fas fa-*
-- Font Awesome (all.min.css) is already loaded and works.
-- Run once in SSMS. Safe to re-run.
-- ============================================================

SET NOCOUNT ON;

UPDATE [dbo].[MenuItems] SET [Icon] = 'fas fa-tachometer-alt'  WHERE [Name] = 'Dashboard';
UPDATE [dbo].[MenuItems] SET [Icon] = 'fas fa-server'          WHERE [Name] = 'Machines';
UPDATE [dbo].[MenuItems] SET [Icon] = 'fas fa-fingerprint'     WHERE [Name] = 'Attendance Machines';
UPDATE [dbo].[MenuItems] SET [Icon] = 'fas fa-clipboard-list'  WHERE [Name] = 'Machine Logs';
UPDATE [dbo].[MenuItems] SET [Icon] = 'fas fa-sync-alt'        WHERE [Name] = 'Force Sync';
UPDATE [dbo].[MenuItems] SET [Icon] = 'fas fa-lock'            WHERE [Name] = 'Machine Lock';
UPDATE [dbo].[MenuItems] SET [Icon] = 'fas fa-calendar-check'  WHERE [Name] = 'Attendance';
UPDATE [dbo].[MenuItems] SET [Icon] = 'fas fa-table'           WHERE [Name] = 'Attendance Records';
UPDATE [dbo].[MenuItems] SET [Icon] = 'fas fa-users'           WHERE [Name] = 'Employees';
UPDATE [dbo].[MenuItems] SET [Icon] = 'fas fa-id-card'         WHERE [Name] = 'Employee List';
UPDATE [dbo].[MenuItems] SET [Icon] = 'fas fa-exchange-alt'    WHERE [Name] = 'Transfer Employee';
UPDATE [dbo].[MenuItems] SET [Icon] = 'fas fa-user-chart'      WHERE [Name] = 'Employee Detail Report';
UPDATE [dbo].[MenuItems] SET [Icon] = 'fas fa-key'             WHERE [Name] = 'Change Password';

PRINT 'Icons updated to Font Awesome.';

-- Verify
SELECT [Name], [Icon] FROM [dbo].[MenuItems] ORDER BY [OrderIndex], [ParentId];
