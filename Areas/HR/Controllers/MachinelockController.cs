using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ServiceHub.Areas.HR.Models;
using ServiceHub.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;

namespace ServiceHub.Areas.HR.Controllers
{
    [Area("HR")]
    [Authorize]
    public class MachineLockController : Controller
    {
        private readonly ServiceHubContext _db;
        private const int OnlineThresholdMinutes = 70;

        public MachineLockController(ServiceHubContext db)
        {
            _db = db;
        }

        // ---------------------------------------------------------------
        //  GET /HR/MachineLock/Index
        // ---------------------------------------------------------------
        public async Task<IActionResult> Index()
        {
            var vm = new MachineLockPageViewModel
            {
                Stores = await _db.Stores
                    .Select(s => new StoreDropdown
                    {
                        StoreCode = s.StoreCode,
                        StoreName = s.StoreName
                    }).OrderBy(s => s.StoreName).ToListAsync(),

                Areas = await _db.Stores.Where(s => s.Area != null).Select(s => s.Area).Distinct().OrderBy(a => a).ToListAsync(),

                Regions = await _db.Stores.Where(s => s.Region != null).Select(s => s.Region).Distinct().OrderBy(r => r).ToListAsync()
            };
            return View(vm);
        }

        // ---------------------------------------------------------------
        //  POST /HR/MachineLock/GetDashboardData  (DataTables server-side)
        // ---------------------------------------------------------------
        [HttpPost]
        public async Task<IActionResult> GetDashboardData()
        {
            var form = HttpContext.Request.Form;
            var draw = form["draw"].FirstOrDefault() ?? "1";
            int start = int.Parse(form["start"].FirstOrDefault() ?? "0");
            int length = int.Parse(form["length"].FirstOrDefault() ?? "25");
            var search = (form["search[value]"].FirstOrDefault() ?? "").ToLower().Trim();
            var fLoc = (form["filterLocation"].FirstOrDefault() ?? "").Trim();
            var fMachine = (form["filterMachine"].FirstOrDefault() ?? "").Trim();
            var fLock = (form["filterLock"].FirstOrDefault() ?? "").Trim();
            var fConn = (form["filterConn"].FirstOrDefault() ?? "").Trim();
            var fArea = (form["filterArea"].FirstOrDefault() ?? "").Trim();
            var fRegion = (form["filterRegion"].FirstOrDefault() ?? "").Trim();

            var rows = await BuildDashboardRowsAsync();

            // Apply filters
            if (!string.IsNullOrEmpty(fLoc))
                rows = rows.Where(r => r.Location == fLoc ||(r.StoreName ?? "").ToLower().Contains(fLoc.ToLower())).ToList();

            if (!string.IsNullOrEmpty(fMachine))
                rows = rows.Where(r =>
                    r.MachineId.ToString() == fMachine ||(r.MachineName ?? "").ToLower().Contains(fMachine.ToLower())).ToList();

            if (!string.IsNullOrEmpty(fLock))
                rows = rows.Where(r => r.LockStatus == fLock).ToList();

            if (!string.IsNullOrEmpty(fConn))
                rows = rows.Where(r => r.ConnectivityStatus == fConn).ToList();

            if (!string.IsNullOrEmpty(fArea))
                rows = rows.Where(r => (r.Area ?? "") == fArea).ToList();

            if (!string.IsNullOrEmpty(fRegion))
                rows = rows.Where(r => (r.Region ?? "") == fRegion).ToList();

            if (!string.IsNullOrEmpty(search))
                rows = rows.Where(r =>(r.MachineName ?? "").ToLower().Contains(search) ||(r.MachineIP ?? "").ToLower().Contains(search) ||(r.StoreName ?? "").ToLower().Contains(search) ||(r.Location ?? "").ToLower().Contains(search) || (r.Area ?? "").ToLower().Contains(search) ||(r.Region ?? "").ToLower().Contains(search)).ToList();

            int total = rows.Count;
            var pageData = rows.Skip(start).Take(length).ToList();

            return Json(new
            {
                draw,
                recordsTotal = total,
                recordsFiltered = total,
                data = pageData.Select(r => new
                {
                    r.MachineId,
                    r.MachineName,
                    r.MachineIP,
                    r.Location,
                    r.StoreName,
                    r.Area,
                    r.Region,
                    r.LockStatus,
                    r.IsLocked,
                    r.ConnectivityStatus,
                    r.IsOnline,
                    lastCommunication = r.LastCommunication.HasValue ? r.LastCommunication.Value.ToString("dd-MMM-yyyy hh:mm tt") : "Never",
                    r.LastCommLabel,
                    lastActionAt = r.LastActionAt.HasValue ? r.LastActionAt.Value.ToString("dd-MMM-yyyy hh:mm tt") : "Never",
                    r.LastActionLabel,
                    r.LastActionBy,
                    r.LastActionType,
                    r.PendingLogId,
                    r.PendingAction,
                    r.HasPending
                })
            });
        }

        // ---------------------------------------------------------------
        //  POST /HR/MachineLock/ApplyAction
        //  Queues a Lock or Unlock command for the Windows service to pick up
        // ---------------------------------------------------------------
        [HttpPost]
        public async Task<IActionResult> ApplyAction([FromBody] LockActionRequest request)
        {
            if (request == null || request.MachineIds == null || request.MachineIds.Count == 0)
                return Json(new LockActionResult { Success = false, Message = "No machines selected." });

            if (request.Action != "Lock" && request.Action != "Unlock")
                return Json(new LockActionResult { Success = false, Message = "Invalid action." });

            string userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "Unknown";
            string userName = User.FindFirstValue(ClaimTypes.Name) ?? User.Identity?.Name?? "Unknown";

            // Validate machines exist
            var validIds = await _db.AttendenceMachines.Where(m => request.MachineIds.Contains(m.Id) && m.IsActive).Select(m => new { m.Id, m.IpAddress }).ToListAsync();

            if (validIds.Count == 0)
                return Json(new LockActionResult { Success = false, Message = "No valid machines found." });

            // Cancel any existing pending requests for the same machines
            var existingPending = await _db.MachineLockLogs.Where(l => request.MachineIds.Contains(l.MachineId) && l.Status == "Pending").ToListAsync();

            foreach (var pending in existingPending)
            {
                pending.Status = "Failed";
                pending.ErrorMessage = "Superseded by newer request.";
                pending.ExecutedAt = DateTime.Now;
            }

            // Insert new Pending log entries for the Windows service to consume
            var logs = validIds.Select(m => new MachineLockLog
            {
                MachineId = m.Id,
                MachineIP = m.IpAddress,
                Action = request.Action,
                RequestedAt = DateTime.Now,
                Status = "Pending",
                RequestedByUserId = userId,
                RequestedByUserName = userName,
                Notes = request.Notes
            }).ToList();

            _db.MachineLockLogs.AddRange(logs);
            await _db.SaveChangesAsync();

            return Json(new LockActionResult
            {
                Success = true,
                Queued = logs.Count,
                Message = $"{request.Action} command queued for {logs.Count} machine(s). " + "The Windows service will execute it momentarily."
            });
        }

        // ---------------------------------------------------------------
        //  GET /HR/MachineLock/GetPendingStatus?logIds=1,2,3
        //  Polls the status of previously queued log entries
        // ---------------------------------------------------------------
        [HttpGet]
        public async Task<IActionResult> GetPendingStatus(string logIds)
        {
            if (string.IsNullOrEmpty(logIds))
                return Json(new { });

            var ids = logIds.Split(',').Select(x => int.TryParse(x, out var i) ? i : 0).Where(i => i > 0).ToList();

            var statuses = await _db.MachineLockLogs.Where(l => ids.Contains(l.Id))
                .Select(l => new
                {
                    l.Id,
                    l.MachineId,
                    l.Status,
                    l.ErrorMessage,
                    executedAt = l.ExecutedAt.HasValue ? l.ExecutedAt.Value.ToString("dd-MMM-yyyy hh:mm tt") : null
                }).ToListAsync();

            return Json(statuses);
        }

        // ---------------------------------------------------------------
        //  GET /HR/MachineLock/GetLockHistory?machineId=5
        // ---------------------------------------------------------------
        [HttpGet]
        public async Task<IActionResult> GetLockHistory(int machineId, int page = 1)
        {
            int pageSize = 15;
            var logs = await _db.MachineLockLogs.Where(l => l.MachineId == machineId).OrderByDescending(l => l.RequestedAt).Skip((page - 1) * pageSize).Take(pageSize).Select(l => new
                {
                    l.Id,
                    l.Action,
                    l.Status,
                    requestedAt = l.RequestedAt.ToString("dd-MMM-yyyy hh:mm tt"),
                    executedAt = l.ExecutedAt.HasValue
                        ? l.ExecutedAt.Value.ToString("dd-MMM-yyyy hh:mm tt") : "Pending",
                    l.RequestedByUserName,
                    l.ErrorMessage,
                    l.Notes
                }).ToListAsync();

            return Json(logs);
        }

        // ===============================================================
        //  PRIVATE: Build dashboard rows
        // ===============================================================
        private async Task<List<MachineLockDashboardRow>> BuildDashboardRowsAsync()
        {
            DateTime onlineThreshold = DateTime.Now.AddMinutes(-OnlineThresholdMinutes);

            // Latest successful attendance sync per machine
            var latestSync = await _db.AttendenceMachineConnectionLogs.GroupBy(l => l.MachineId).Select(g => g.OrderByDescending(l => l.Id).FirstOrDefault()).ToListAsync();
            var syncDict = latestSync.Where(l => l != null).ToDictionary(l => l.MachineId);

            // Latest SUCCESSFUL lock action per machine
            var latestLock = await _db.MachineLockLogs.Where(l => l.Status == "Success").GroupBy(l => l.MachineId).Select(g => g.OrderByDescending(l => l.ExecutedAt).FirstOrDefault()).ToListAsync();
            var lockDict = latestLock.Where(l => l != null).ToDictionary(l => l.MachineId);

            // Latest PENDING action per machine
            var pendingLock = await _db.MachineLockLogs.Where(l => l.Status == "Pending").GroupBy(l => l.MachineId).Select(g => g.OrderByDescending(l => l.RequestedAt).FirstOrDefault()).ToListAsync();
            var pendingDict = pendingLock.Where(l => l != null).ToDictionary(l => l.MachineId);

            // Machines joined with stores
            var machines = await _db.AttendenceMachines.Where(m => m.IsActive).ToListAsync();

            // Store lookup by StoreCode = machine.Location
            var storeCodes = machines.Where(m => m.Location != null).Select(m => m.Location).Distinct().ToList();

            var stores = await _db.Stores.Where(s => storeCodes.Contains(s.StoreCode)).ToListAsync();
            var storeDict = stores.ToDictionary(s => s.StoreCode);

            var rows = new List<MachineLockDashboardRow>();

            foreach (var m in machines)
            {
                syncDict.TryGetValue(m.Id, out var sync);
                lockDict.TryGetValue(m.Id, out var lockLog);
                pendingDict.TryGetValue(m.Id, out var pendLog);
                storeDict.TryGetValue(m.Location ?? "", out var store);

                bool online = sync != null && sync.Status == "Success" && sync.Connection_StartTime >= onlineThreshold;

                // Lock status: last completed action wins; null = never actioned = Unlocked by default
                string lockStatus = lockLog != null? (lockLog.Action == "Lock" ? "Locked" : "Unlocked"): "Unlocked";

                DateTime? lastComm = sync?.Connection_StartTime;
                DateTime? lastAct = lockLog?.ExecutedAt;

                rows.Add(new MachineLockDashboardRow
                {
                    MachineId = m.Id,
                    MachineName = m.Name,
                    MachineIP = m.IpAddress,
                    Port = m.Port,
                    Location = m.Location,
                    StoreName = store?.StoreName,
                    Area = store?.Area,
                    Region = store?.Region,
                    Department = store?.Department,
                    LockStatus = lockStatus,
                    ConnectivityStatus = online ? "Online" : "Offline",
                    LastCommunication = lastComm,
                    LastCommLabel = lastComm.HasValue ? GetTimeAgo(lastComm.Value) : "Never",
                    LastActionAt = lastAct,
                    LastActionLabel = lastAct.HasValue ? GetTimeAgo(lastAct.Value) : "Never",
                    LastActionBy = lockLog?.RequestedByUserName,
                    LastActionType = lockLog?.Action,
                    PendingLogId = pendLog?.Id,
                    PendingAction = pendLog?.Action
                });
            }

            return rows.OrderBy(r => r.StoreName).ThenBy(r => r.MachineName).ToList();
        }

        private static string GetTimeAgo(DateTime past)
        {
            var span = DateTime.Now - past;
            if (span.TotalMinutes < 1) return "Just now";
            if (span.TotalMinutes < 60) return $"{(int)span.TotalMinutes} min ago";
            if (span.TotalHours < 24) return $"{(int)span.TotalHours} hr ago";
            return $"{(int)span.TotalDays} day(s) ago";
        }
    }
}