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

        public HomeController(ILogger<HomeController> logger,UserManager<ApplicationUser> userManager, ServiceHubContext context)
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
            var today = DateTime.Today;
            var tomorrow = today.AddDays(1);

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

            var filteredIPs = !string.IsNullOrEmpty(machineIP) ? new string[] { machineIP } : filteredMachines.Select(m => m.IpAddress).ToArray();


            // ── Date range for month/year filter ────────────────────────────
            var startDate = new DateTime(year, month, 1);
            var endDate = startDate.AddMonths(1).AddDays(-1);

            // ── Connection logs (today) ─────────────────────────────────
            var connLogsQuery = _db.AttendenceMachineConnectionLogs.AsNoTracking().Where(l => l.Connection_StartTime >= today && l.Connection_StartTime < tomorrow && l.Machine_IP != null && filteredIPs.Contains(l.Machine_IP));

            int successCount = connLogsQuery.Count(l => l.Status == "Success");
            int failCount = connLogsQuery.Count(l => l.Status == "Failed");

            // ── Records fetched (selected month/year) ────────────────────

            var swapQuery = _db.HR_Swap_Record.AsNoTracking().Where(r => r.Swap_Time.HasValue &&
                   r.Swap_Time.Value >= startDate &&
                   r.Swap_Time.Value <= endDate.AddDays(1).AddTicks(-1) &&
                   r.Machine_IP != null &&
                   filteredIPs.Contains(r.Machine_IP));

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
            var recordsByDate = swapQuery.GroupBy(r => r.Swap_Time!.Value.Date).Select(g => new { Date = g.Key, Count = g.Count() }).ToList();

            var lookup = recordsByDate.ToDictionary(r => r.Date, r => r.Count);

            var labels = new List<string>();
            var data = new List<int>();
            for (var d = startDate; d <= endDate; d = d.AddDays(1))
            {
                labels.Add(d.ToString("dd MMM"));
                data.Add(lookup.TryGetValue(d, out int cnt) ? cnt : 0);
            }

            // ── Per-machine breakdown (donut chart) ──────────────────────
            var machDict = allMachines.GroupBy(m => m.IpAddress ?? "").ToDictionary(g => g.Key, g => g.First().Name ?? "");
            var machBreak = swapQuery
                            .GroupBy(r => r.Machine_IP!)
                            .Select(g => new { IP = g.Key, Count = g.Count() })
                            .OrderByDescending(g => g.Count)
                            .Take(10)
                            .ToList();

            var mNames  = machBreak.Select(x => machDict.TryGetValue(x.IP, out var n) ? n : x.IP).ToList();
            var mCounts = machBreak.Select(x => x.Count).ToList();

            // ── Recent activity (last 20 punches) ────────────────────────         
            var recent = _db.HR_Swap_Record
                         .AsNoTracking()
                         .Where(r => r.Swap_Time.HasValue &&
                                     r.Swap_Time.Value >= startDate &&
                                     r.Swap_Time.Value <= endDate.AddDays(1).AddTicks(-1) &&
                                     r.Machine_IP != null &&
                                     filteredIPs.Contains(r.Machine_IP))
                         .OrderByDescending(r => r.PK_line_id)
                         .Take(20)
                         .ToList()
                         .Select(r =>
                         {
                             machDict.TryGetValue(r.Machine_IP ?? "", out var mName);
                             return new RecentActivityRow
                             {
                                 EmpNo = r.Emp_No ?? "",
                                 EmpName = r.Emp_Name ?? "",
                                 SwapTime = r.Swap_Time,
                                 MachineName = mName ?? r.Machine_IP ?? "",
                                 Direction = r.Shift_In == true ? "In" : r.Shift_Out == true ? "Out" : "–"
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
