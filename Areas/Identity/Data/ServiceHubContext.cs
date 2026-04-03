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
    public DbSet<ServiceHub.Areas.HR.Models.EmployeeEnrollment> EmployeeEnrollments { get; set; }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);        
        // Simple index for faster lookups
        builder.Entity<ServiceHub.Areas.HR.Models.EmployeeEnrollment>(b =>
        {
            b.HasIndex(e => new { e.EmployeeCode, e.MachineId }).IsUnique(false);
        });

    }
}
