using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ServiceHub.Areas.HR.Models
{
    public class MachineLockLog
    {
        [Key]
        public int Id { get; set; }
        public int MachineId { get; set; }
        public string MachineIP { get; set; }
        public string Action { get; set; }   // "Lock" | "Unlock"
        public DateTime RequestedAt { get; set; } = DateTime.Now;
        public DateTime? ExecutedAt { get; set; }
        public string Status { get; set; } = "Pending"; // Pending|Success|Failed
        public string ErrorMessage { get; set; }
        public string RequestedByUserId { get; set; }
        public string RequestedByUserName { get; set; }
        public string Notes { get; set; }

        // Navigation
        [ForeignKey(nameof(MachineId))]
        public virtual AttendanceMachine Machine { get; set; }
    }
}
