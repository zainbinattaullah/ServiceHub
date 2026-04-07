namespace ServiceHub.Areas.HR.Models
{
    public class DashboardViewModel
    {
        // ── Summary cards ────────────────────────────────────────────────
        public int SuccessfullyConnected { get; set; }
        public int FailedConnections { get; set; }
        public int TotalRecordsFetched { get; set; }
        public int ActiveMachines { get; set; }
        public int InactiveMachines { get; set; }
        public int TotalEmployees { get; set; }
        public int EnrolledEmployees { get; set; }

        // ── Chart data ───────────────────────────────────────────────────
        public List<string> ChartLabels { get; set; } = new();
        public List<int> ChartData { get; set; } = new();

        // ── Machine breakdown for donut chart ────────────────────────────
        public List<string> MachineNames { get; set; } = new();
        public List<int> MachineRecordCounts { get; set; } = new();

        // ── Filters (current state passed to view) ───────────────────────
        public int FilterMonth { get; set; }
        public int FilterYear { get; set; }
        public string? FilterMachineIP { get; set; }
        public string? FilterStatus { get; set; }   // "all" | "active" | "inactive"

        // ── Machine list (for filter dropdown) ───────────────────────────
        public List<MachineFilterItem> Machines { get; set; } = new();

        // ── Recent activity feed ─────────────────────────────────────────
        public List<RecentActivityRow> RecentActivity { get; set; } = new();
    }

    public class MachineFilterItem
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public string IpAddress { get; set; } = "";
        public bool IsActive { get; set; }
    }

    public class RecentActivityRow
    {
        public string EmpNo { get; set; } = "";
        public string EmpName { get; set; } = "";
        public DateTime? SwapTime { get; set; }
        public string MachineName { get; set; } = "";
        public string Direction { get; set; } = "";
    }
}
