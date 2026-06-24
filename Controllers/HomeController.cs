using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ServiceHub.Areas.HR.Models;
using ServiceHub.Data;
using ServiceHub.Models;
using System.Diagnostics;
using System.Threading;

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
        public IActionResult Index(DateTime? date, string? machineIP, string? status)
        {
            ViewData["User_Id"] = _userManager.GetUserId(User);

            var d = date ?? DateTime.Today;

            var vm = BuildDashboard(d, machineIP, status);
            return View(vm);
        }

        // ================================================================
        //  GET /Home/DashboardData  (AJAX — filter change without reload)
        // ================================================================
        [HttpGet]
        public IActionResult DashboardData(DateTime? date, string? machineIP, string? status)
        {
            var d = date ?? DateTime.Today;
            var vm = BuildDashboard(d, machineIP, status);
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

        [HttpGet]
        public IActionResult TotalEmployeesDetails()
        {
            var employees = _db.EmployeeEnrollments
                .AsNoTracking()
                .Where(e => e.EmployeeCode != null)
                .GroupBy(e => e.EmployeeCode)
                .Select(g => g.OrderByDescending(x => x.CreatedAt).FirstOrDefault())
                .ToList();

            var machines = _db.AttendenceMachines.AsNoTracking().ToDictionary(m => m.Id, m => m.Name ?? m.IpAddress ?? "");

            var result = employees.Select(e => new {
                empNo = e.EmployeeCode,
                empName = e.EmployeeName ?? "—",
                machineName = e.MachineId.HasValue && machines.TryGetValue(e.MachineId.Value, out var mName) ? mName : (e.MachineIP ?? "—"),
                isSynced = e.IsSynced,
                syncStatus = e.IsSynced ? "Synced" : "Pending",
                createdAt = e.CreatedAt.ToString("dd MMM yyyy HH:mm")
            }).OrderBy(e => e.empName).ToList();

            return Json(result);
        }

        [HttpGet]
        public IActionResult EnrolledEmployeesDetails()
        {
            var enrolled = _db.Employee_Biometric_Log
                .AsNoTracking()
                .Where(b => b.IsActive && b.EmpNo != null)
                .GroupBy(b => b.EmpNo)
                .Select(g => g.OrderByDescending(x => x.EnrollmentDate).FirstOrDefault())
                .ToList();

            var empNos = enrolled.Select(e => e.EmpNo).ToList();

            // Get all machine names for lookup
            var machines = _db.AttendenceMachines
                            .AsNoTracking()
                            .GroupBy(m => m.IpAddress ?? "")
                            .ToDictionary(
                                g => g.Key,
                                g => g.Select(m => m.Name ?? m.IpAddress ?? "").First()
                            );

            var latestSwaps = _db.HR_Swap_Record
                .AsNoTracking()
                .Where(s => empNos.Contains(s.Emp_No))
                .GroupBy(s => s.Emp_No)
                .Select(g => g.OrderByDescending(s => s.Swap_Time).FirstOrDefault())
                .ToDictionary(s => s.Emp_No, s => s);

            var result = enrolled.Select(e => {
                var hasSwap = latestSwaps.TryGetValue(e.EmpNo ?? "", out var s);
                string lastMachine = "—";
                if (hasSwap && s!.Machine_IP != null)
                {
                    machines.TryGetValue(s.Machine_IP, out lastMachine);
                }

                return new {
                    empNo = e.EmpNo,
                    empName = e.EmpName,
                    enrolledAt = e.MachineName ?? "—",
                    lastSeenAt = lastMachine,
                    fingers = e.TotalFingersEnrolled,
                    enrollDate = e.EnrollmentDate.ToString("dd MMM yyyy"),
                    latestSwap = hasSwap ? s!.Swap_Time?.ToString("dd MMM yyyy HH:mm") : "No record",
                    direction = hasSwap ? (s!.Shift_In == true ? "In" : s.Shift_Out == true ? "Out" : "–") : "–"
                };
            }).OrderBy(e => e.empName).ToList();

            return Json(result);
        }

        // ================================================================
        //  Private: build dashboard view-model with filters
        // ================================================================
        private DashboardViewModel BuildDashboard(DateTime date, string? machineIP, string? status)
        {
            var targetDay = date.Date;
            var nextDay   = targetDay.AddDays(1);

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


            // ── Connection logs (selected day) ──────────────────────────
            var connLogsQuery = _db.AttendenceMachineConnectionLogs.AsNoTracking()
                .Where(l => l.Connection_StartTime >= targetDay && 
                            l.Connection_StartTime < nextDay && 
                            l.Machine_IP != null && 
                            filteredIPs.Contains(l.Machine_IP));

            int successCount = connLogsQuery.Count(l => l.Status == "Success");
            int failCount = connLogsQuery.Count(l => l.Status == "Failed");

            // ── Records fetched (selected day) ───────────────────────────
            var swapQuery = _db.HR_Swap_Record.AsNoTracking().Where(r => r.Swap_Time.HasValue &&
                   r.Swap_Time.Value >= targetDay &&
                   r.Swap_Time.Value < nextDay &&
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

            // ── Hourly chart (selected day) ──────────────────────────────
            var recordsByHour = swapQuery
                .GroupBy(r => r.Swap_Time!.Value.Hour)
                .Select(g => new { Hour = g.Key, Count = g.Count() })
                .ToList();

            var lookup = recordsByHour.ToDictionary(r => r.Hour, r => r.Count);

            var labels = new List<string>();
            var data = new List<int>();
            for (int h = 0; h < 24; h++)
            {
                labels.Add($"{h:D2}:00");
                data.Add(lookup.TryGetValue(h, out int cnt) ? cnt : 0);
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
                                     r.Swap_Time.Value >= targetDay &&
                                     r.Swap_Time.Value < nextDay &&
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
                FilterDate            = targetDay,
                FilterMonth           = targetDay.Month,
                FilterYear            = targetDay.Year,
                FilterMachineIP       = machineIP,
                FilterStatus          = status ?? "all",
                Machines              = machineFilter,
                RecentActivity        = recent
            };
        }

        [HttpGet]
        public async Task<IActionResult> RefreshConnectivity()
        {
            var machines = await _db.AttendenceMachines
                .Where(m => m.IsActive && (m.SerialNumber == null || m.SerialNumber == ""))
                .Select(m => new { m.IpAddress, m.Port, m.Name })
                .ToListAsync();

            var semaphore = new SemaphoreSlim(10);
            var tasks = machines.Select(async m =>
            {
                await semaphore.WaitAsync();
                try
                {
                    bool online = await Task.Run(() => IsMachineReachable(m.IpAddress, m.Port));
                    return new { m.IpAddress, m.Name, Online = online };
                }
                finally { semaphore.Release(); }
            });

            var results = await Task.WhenAll(tasks);
            return Json(new
            {
                online  = results.Count(r => r.Online),
                offline = results.Count(r => !r.Online),
                details = results
            });
        }

        private static bool IsMachineReachable(string ip, int port = 4370)
        {
            try
            {
                using var icmp = new System.Net.NetworkInformation.Ping();
                var reply = icmp.Send(ip, 2000);
                if (reply?.Status == System.Net.NetworkInformation.IPStatus.Success)
                    return true;
            }
            catch { }
            try
            {
                using var tcp = new System.Net.Sockets.TcpClient();
                var ar = tcp.BeginConnect(ip, port, null, null);
                if (ar.AsyncWaitHandle.WaitOne(TimeSpan.FromSeconds(2)) && tcp.Connected)
                {
                    tcp.EndConnect(ar);
                    return true;
                }
            }
            catch { }
            return false;
        }

        public IActionResult Privacy() => View();

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
            => View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
