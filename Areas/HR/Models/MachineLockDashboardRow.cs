namespace ServiceHub.Areas.HR.Models
{
    public class MachineLockDashboardRow
    {
        public int MachineId { get; set; }
        public string MachineName { get; set; }
        public string MachineIP { get; set; }
        public int Port { get; set; }

        // From Stores join
        public string Location { get; set; }   // StoreCode on machine
        public string StoreName { get; set; }
        public string Area { get; set; }
        public string Region { get; set; }

        // Lock state — derived from latest Success log
        public string LockStatus { get; set; }   // "Locked" | "Unlocked" | "Unknown"
        public bool IsLocked => LockStatus == "Locked";

        // Online state — same threshold as MachineHealth (last 70 min success sync)
        public string ConnectivityStatus { get; set; }   // "Online" | "Offline"
        public bool IsOnline => ConnectivityStatus == "Online";

        // Last attendance sync
        public DateTime? LastCommunication { get; set; }
        public string LastCommLabel { get; set; }

        // Last lock/unlock action
        public DateTime? LastActionAt { get; set; }
        public string LastActionLabel { get; set; }
        public string LastActionBy { get; set; }
        public string LastActionType { get; set; }   // "Lock" | "Unlock"

        // Pending action waiting for service
        public int? PendingLogId { get; set; }
        public string PendingAction { get; set; }
        public bool HasPending => PendingLogId.HasValue;
    }
}
