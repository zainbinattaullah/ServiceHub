using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using ServiceHub.Areas.HR.Models;

namespace ServiceHub.Data;

public class ServiceHubContext : IdentityDbContext<ApplicationUser>
{
    public ServiceHubContext(DbContextOptions<ServiceHubContext> options)
        : base(options)
    {
    }

    // ── Existing Tables ──────────────────────────────────────────────────
    public DbSet<AttendanceMachine> AttendenceMachines { get; set; }
    public DbSet<AttendanceMachineConnectionLog> AttendenceMachineConnectionLogs { get; set; }
    public DbSet<HRSwapRecord> HR_Swap_Record { get; set; }
    public DbSet<PasswordChangeLog> PasswordChangeLog { get; set; }
    public DbSet<EmployeeEnrollment> EmployeeEnrollments { get; set; }
    public DbSet<MachineLockLog> MachineLockLogs { get; set; }
    public DbSet<ForceSyncLog> ForceSyncLogs { get; set; }
    public DbSet<Store> Stores { get; set; }
    public DbSet<MachineFormatLog> MachineFormatLogs { get; set; }
    public DbSet<Employee_Biometric_Log> Employee_Biometric_Log { get; set; }
    public DbSet<Department> Departments { get; set; }

    // ── RBAC / Theme Tables ──────────────────────────────────────────────
    public DbSet<MenuItem> MenuItems { get; set; }
    public DbSet<RoleMenuItem> RoleMenuItems { get; set; }
    public DbSet<UserSetting> UserSettings { get; set; }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // ── Existing index ───────────────────────────────────────────────
        builder.Entity<EmployeeEnrollment>(b =>
        {
            b.HasIndex(e => new { e.EmployeeCode, e.MachineId }).IsUnique(false);
        });

        // ── MenuItem self-referencing hierarchy ──────────────────────────
        builder.Entity<MenuItem>(b =>
        {
            b.HasOne(m => m.Parent)
             .WithMany(m => m.Children)
             .HasForeignKey(m => m.ParentId)
             .OnDelete(DeleteBehavior.Restrict);

            b.HasIndex(m => m.OrderIndex);
        });

        // ── RoleMenuItem composite uniqueness ────────────────────────────
        builder.Entity<RoleMenuItem>(b =>
        {
            b.HasIndex(rm => new { rm.RoleId, rm.MenuItemId }).IsUnique();

            b.HasOne(rm => rm.MenuItem)
             .WithMany(m => m.RoleMenuItems)
             .HasForeignKey(rm => rm.MenuItemId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        // ── UserSetting — one row per user ───────────────────────────────
        builder.Entity<UserSetting>(b =>
        {
            b.HasIndex(u => u.UserId).IsUnique();
        });
    }
}
