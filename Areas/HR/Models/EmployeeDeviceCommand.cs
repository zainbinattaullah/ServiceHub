using System.ComponentModel.DataAnnotations;

namespace ServiceHub.Areas.HR.Models
{
    public class EmployeeDeviceCommand
    {
        [Key]
        public int Id { get; set; }

        [Required, MaxLength(50)]
        public string EmployeeCode { get; set; }

        [MaxLength(250)]
        public string EmployeeName { get; set; }

        /// <summary>"Activate" or "Deactivate"</summary>
        [Required, MaxLength(20)]
        public string Action { get; set; }

        /// <summary>"Pending" | "InProgress" | "Success" | "Failed"</summary>
        [MaxLength(20)]
        public string Status { get; set; } = "Pending";

        public DateTime RequestedAt { get; set; } = DateTime.Now;

        [MaxLength(450)]
        public string RequestedByUserId { get; set; }

        [MaxLength(450)]
        public string RequestedByUserName { get; set; }

        public DateTime? ProcessedAt { get; set; }
        public int? MachinesAttempted { get; set; }
        public int? MachinesSucceeded { get; set; }
        public string ErrorMessage { get; set; }
    }
}
