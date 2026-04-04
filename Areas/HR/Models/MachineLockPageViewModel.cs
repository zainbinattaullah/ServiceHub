namespace ServiceHub.Areas.HR.Models
{
    public class MachineLockPageViewModel
    {
        public List<StoreDropdown> Stores { get; set; } = new();
        public List<string> Areas { get; set; } = new();
        public List<string> Regions { get; set; } = new();
    }
    public class StoreDropdown
    {
        public string StoreCode { get; set; }
        public string StoreName { get; set; }
    }
    public class LockActionRequest
    {
        public List<int> MachineIds { get; set; } = new();
        public string Action { get; set; }  // "Lock" | "Unlock"
        public string Notes { get; set; }
    }

    public class LockActionResult
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        public int Queued { get; set; }
    }
}
