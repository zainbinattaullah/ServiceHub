using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ServiceHub.Areas.HR.Models
{
    public class ForceSyncLog
    {
        [Key]
        public int Id { get; set; }
        public int MachineId { get; set; }
        public string MachineIP { get; set; }
        public string SyncType { get; set; } = "Manual";
        public string Status { get; set; } = "Pending";
        public DateTime RequestedAt { get; set; } = DateTime.Now;
        public DateTime? StartedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public int? DurationSeconds { get; set; }
        public int? RecordsFetched { get; set; }
        public string? ErrorMessage { get; set; }
        public string RequestedByUserId { get; set; }
        public string RequestedByUserName { get; set; }
        public string? Notes { get; set; }

        [ForeignKey(nameof(MachineId))]
        public virtual AttendanceMachine Machine { get; set; }
    }
}
