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
    public DbSet<AttendanceMachine> AttendenceMachines { get; set; }
    public DbSet<AttendanceMachineConnectionLog> AttendenceMachineConnectionLogs { get; set; }
    public DbSet<HRSwapRecord> HR_Swap_Record { get; set; }
    public DbSet<PasswordChangeLog> PasswordChangeLog { get; set; }
    public DbSet<EmployeeEnrollment> EmployeeEnrollments { get; set; }
    public DbSet<MachineLockLog> MachineLockLogs { get; set; }
    public DbSet<ForceSyncLog> ForceSyncLogs { get; set; }
    public DbSet<Store> Stores { get; set; }
    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);        
        // Simple index for faster lookups
        builder.Entity<EmployeeEnrollment>(b =>
        {
            b.HasIndex(e => new { e.EmployeeCode, e.MachineId }).IsUnique(false);
        });

    }
}
