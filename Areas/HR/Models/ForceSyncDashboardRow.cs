namespace ServiceHub.Areas.HR.Models
{
    // ---------------------------------------------------------------
    //  Dashboard row — one machine with its current sync state
    // ---------------------------------------------------------------
    public class ForceSyncDashboardRow
    {
        public int MachineId { get; set; }
        public string MachineName { get; set; }
        public string MachineIP { get; set; }
        public int Port { get; set; }
        public string Location { get; set; }
        public string StoreName { get; set; }
        public string Area { get; set; }
        public string Region { get; set; }

        // Connectivity — last 70 min success sync
        public string ConnectivityStatus { get; set; }
        public bool IsOnline => ConnectivityStatus == "Online";

        // Last sync (Auto or Manual — whichever is most recent)
        public DateTime? LastSyncAt { get; set; }
        public string LastSyncLabel { get; set; }
        public string LastSyncStatus { get; set; }  // Success|Failed|Pending|InProgress
        public string LastSyncType { get; set; }  // Auto|Manual
        public int? LastSyncDuration { get; set; }  // seconds
        public string LastSyncDurationLabel { get; set; }
        public int? LastRecordsFetched { get; set; }

        // Latest pending force sync if any
        public int? PendingForceSyncId { get; set; }
        public string PendingStatus { get; set; }
        public bool HasPending => PendingForceSyncId.HasValue;
    }
    // ---------------------------------------------------------------
    //  Live monitor panel counters (returned in AJAX response)
    // ---------------------------------------------------------------
    public class ForceSyncMonitorStats
    {
        public int TotalSelected { get; set; }
        public int Completed { get; set; }
        public int InProgress { get; set; }
        public int Failed { get; set; }
        public int Pending { get; set; }
    }
    // ---------------------------------------------------------------
    //  Page viewmodel
    // ---------------------------------------------------------------
    public class ForceSyncPageViewModel
    {
        public List<StoreDropdown> Stores { get; set; } = new();
        public List<string> Areas { get; set; } = new();
        public List<string> Regions { get; set; } = new();
    }
    // ---------------------------------------------------------------
    //  API models
    // ---------------------------------------------------------------
    public class ForceSyncRequest
    {
        public List<int> MachineIds { get; set; } = new();
        public string Notes { get; set; }
    }

    public class ForceSyncQueueResult
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        public int Queued { get; set; }
        public List<int> LogIds { get; set; } = new();
    }

}
