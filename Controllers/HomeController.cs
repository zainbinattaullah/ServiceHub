using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ServiceHub.Areas.HR.Models;
using ServiceHub.Data;
using ServiceHub.Models;
using System.Diagnostics;

namespace ServiceHub.Controllers
{
    [Authorize]
    public class HomeController : Controller
    {
        private readonly ServiceHubContext _db;
        private readonly ILogger<HomeController> _logger;
        private readonly UserManager<ApplicationUser> _userManager;

        public HomeController(ILogger<HomeController> logger,
                              UserManager<ApplicationUser> userManager,
                              ServiceHubContext context)
        {
            _logger      = logger;
            _userManager  = userManager;
            _db           = context;
        }

        // ================================================================
        //  GET /Home/Index  (Dashboard — full page load)
        // ================================================================
        public IActionResult Index(int? month, int? year, string? machineIP, string? status)
        {
            ViewData["User_Id"] = _userManager.GetUserId(User);

            int m = month ?? DateTime.Now.Month;
            int y = year  ?? DateTime.Now.Year;

            var vm = BuildDashboard(m, y, machineIP, status);
            return View(vm);
        }

        // ================================================================
        //  GET /Home/DashboardData  (AJAX — filter change without reload)
        // ================================================================
        [HttpGet]
        public IActionResult DashboardData(int month, int year, string? machineIP, string? status)
        {
            var vm = BuildDashboard(month, year, machineIP, status);
            return Json(new
            {
                successfullyConnected = vm.SuccessfullyConnected,
                failedConnections     = vm.FailedConnections,
                totalRecordsFetched   = vm.TotalRecordsFetched,
                activeMachines        = vm.ActiveMachines,
                inactiveMachines      = vm.InactiveMachines,
                totalEmployees        = vm.TotalEmployees,
                enrolledEmployees     = vm.EnrolledEmployees,
                chartLabels           = vm.ChartLabels,
                chartData             = vm.ChartData,
                machineNames          = vm.MachineNames,
                machineRecordCounts   = vm.MachineRecordCounts,
                recentActivity        = vm.RecentActivity.Select(a => new
                {
                    a.EmpNo, a.EmpName, a.MachineName, a.Direction,
                    swapTime = a.SwapTime?.ToString("dd MMM yyyy HH:mm")
                })
            });
        }

        // ================================================================
        //  Private: build dashboard view-model with filters
        // ================================================================
        private DashboardViewModel BuildDashboard(int month, int year, string? machineIP, string? status)
        {
            var today = DateTime.Now.Date;

            // ── Machine filter list ─────────────────────────────────────
            var allMachines = _db.AttendenceMachines.AsNoTracking().ToList();
            var machineFilter = allMachines.Select(m => new MachineFilterItem
            {
                Id = m.Id, Name = m.Name, IpAddress = m.IpAddress, IsActive = m.IsActive
            }).ToList();

            // Apply machine status filter
            IQueryable<AttendanceMachine> filteredMachines = _db.AttendenceMachines.AsNoTracking();
            if (status == "active")
                filteredMachines = filteredMachines.Where(m => m.IsActive);
            else if (status == "inactive")
                filteredMachines = filteredMachines.Where(m => !m.IsActive);

            var filteredIPs = !string.IsNullOrEmpty(machineIP)
                ? new List<string> { machineIP }
                : filteredMachines.Select(m => m.IpAddress).ToList();

            // ── Connection logs (today) ─────────────────────────────────
            var connLogs = _db.AttendenceMachineConnectionLogs
                .Where(l => l.Connection_StartTime.Date == today);

            if (filteredIPs.Any())
                connLogs = connLogs.Where(l => l.Machine_IP != null && filteredIPs.Contains(l.Machine_IP));

            int successCount = connLogs.Count(l => l.Status == "Success");
            int failCount    = connLogs.Count(l => l.Status == "Failed");

            // ── Records fetched (selected month/year) ────────────────────
            var startDate = new DateTime(year, month, 1);
            var endDate   = startDate.AddMonths(1).AddDays(-1);

            var swapQuery = _db.HR_Swap_Record
                .Where(r => r.Creation_Date.HasValue &&
                            r.Creation_Date.Value >= startDate &&
                            r.Creation_Date.Value <= endDate);

            if (filteredIPs.Any())
                swapQuery = swapQuery.Where(r => r.Machine_IP != null && filteredIPs.Contains(r.Machine_IP));

            int totalFetched = swapQuery.Count();

            // ── Machine status counts ────────────────────────────────────
            int activeMachines   = allMachines.Count(m => m.IsActive);
            int inactiveMachines = allMachines.Count(m => !m.IsActive);

            // ── Employee counts ──────────────────────────────────────────
            int totalEmps = _db.EmployeeEnrollments.AsNoTracking()
                .Where(e => e.EmployeeCode != null)
                .Select(e => e.EmployeeCode).Distinct().Count();

            int enrolledEmps = _db.Employee_Biometric_Log.AsNoTracking()
                .Where(b => b.IsActive && b.EmpNo != null)
                .Select(b => b.EmpNo).Distinct().Count();

            // ── Day-wise chart (selected month) ──────────────────────────
            var recordsByDate = swapQuery
                .GroupBy(r => r.Creation_Date!.Value.Date)
                .Select(g => new { Date = g.Key, Count = g.Count() })
                .ToList();

            var lookup = recordsByDate.ToDictionary(r => r.Date, r => r.Count);

            var labels = new List<string>();
            var data   = new List<int>();
            for (var d = startDate; d <= endDate; d = d.AddDays(1))
            {
                labels.Add(d.ToString("dd MMM"));
                data.Add(lookup.TryGetValue(d, out int cnt) ? cnt : 0);
            }

            // ── Per-machine breakdown (donut chart) ──────────────────────
            var machDict = allMachines.ToDictionary(m => m.IpAddress ?? "", m => m.Name);
            var machBreak = swapQuery
                .Where(r => r.Machine_IP != null)
                .GroupBy(r => r.Machine_IP!)
                .Select(g => new { IP = g.Key, Count = g.Count() })
                .OrderByDescending(g => g.Count)
                .Take(10)
                .ToList();

            var mNames  = machBreak.Select(x => machDict.TryGetValue(x.IP, out var n) ? n : x.IP).ToList();
            var mCounts = machBreak.Select(x => x.Count).ToList();

            // ── Recent activity (last 15 punches) ────────────────────────
            var recent = _db.HR_Swap_Record.AsNoTracking()
                .Where(r => r.Swap_Time != null)
                .OrderByDescending(r => r.Swap_Time)
                .Take(15)
                .ToList()
                .Select(r =>
                {
                    machDict.TryGetValue(r.Machine_IP ?? "", out var mName);
                    return new RecentActivityRow
                    {
                        EmpNo       = r.Emp_No ?? "",
                        EmpName     = r.Emp_Name ?? "",
                        SwapTime    = r.Swap_Time,
                        MachineName = mName ?? r.Machine_IP ?? "",
                        Direction   = r.Shift_In == true ? "In" : r.Shift_Out == true ? "Out" : "–"
                    };
                }).ToList();

            return new DashboardViewModel
            {
                SuccessfullyConnected = successCount,
                FailedConnections     = failCount,
                TotalRecordsFetched   = totalFetched,
                ActiveMachines        = activeMachines,
                InactiveMachines      = inactiveMachines,
                TotalEmployees        = totalEmps,
                EnrolledEmployees     = enrolledEmps,
                ChartLabels           = labels,
                ChartData             = data,
                MachineNames          = mNames,
                MachineRecordCounts   = mCounts,
                FilterMonth           = month,
                FilterYear            = year,
                FilterMachineIP       = machineIP,
                FilterStatus          = status ?? "all",
                Machines              = machineFilter,
                RecentActivity        = recent
            };
        }

        public IActionResult Privacy() => View();

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
            => View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
