using System.ComponentModel.DataAnnotations;

namespace ServiceHub.Areas.HR.Models
{
    public class EmployeeEnrollment
    {
        public int Id { get; set; }

        [Required]
        [StringLength(50)]
        public string EmployeeCode { get; set; }

        [StringLength(250)]
        public string? EmployeeName { get; set; }

        // optional FK to AttendanceMachine
        public int? MachineId { get; set; }

        [StringLength(50)]
        public string? MachineIP { get; set; }

        [StringLength(100)]
        public string? Privilege { get; set; }

        [StringLength(450)]
        public string? CreatedBy { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public bool IsActive { get; set; } = true;

        // Whether the record was successfully sent to the device/service
        public bool IsSynced { get; set; } = false;

        public string? SyncMessage { get; set; }

        public DateTime? SyncedAt { get; set; }
        public int? DepartmentId { get; set; }
    }
}
