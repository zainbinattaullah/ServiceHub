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
    public class ForceSyncController : Controller
    {
        private readonly ServiceHubContext _db;
        private const int OnlineThresholdMinutes = 70;

        public ForceSyncController(ServiceHubContext db)
        {
            _db = db;
        }

        // ---------------------------------------------------------------
        //  GET /HR/ForceSync/Index
        // ---------------------------------------------------------------
        public async Task<IActionResult> Index()
        {
            var vm = new ForceSyncPageViewModel
            {
                Stores = await _db.Stores
                    .Select(s => new StoreDropdown
                    {
                        StoreCode = s.StoreCode,
                        StoreName = s.StoreName
                    })
                    .OrderBy(s => s.StoreName)
                    .ToListAsync(),

                Areas = await _db.Stores
                    .Where(s => s.Area != null)
                    .Select(s => s.Area)
                    .Distinct().OrderBy(a => a).ToListAsync(),

                Regions = await _db.Stores
                    .Where(s => s.Region != null)
                    .Select(s => s.Region)
                    .Distinct().OrderBy(r => r).ToListAsync()
            };
            return View(vm);
        }

        // ---------------------------------------------------------------
        //  POST /HR/ForceSync/GetDashboardData  (DataTables server-side)
        // ---------------------------------------------------------------
        [HttpPost]
        public async Task<IActionResult> GetDashboardData()
        {
            var form = HttpContext.Request.Form;
            var draw = form["draw"].FirstOrDefault() ?? "1";
            int start = int.Parse(form["start"].FirstOrDefault() ?? "0");
            int length = int.Parse(form["length"].FirstOrDefault() ?? "25");
            var search = (form["search[value]"].FirstOrDefault() ?? "").ToLower().Trim();
            var fRegion = (form["filterRegion"].FirstOrDefault() ?? "").Trim();
            var fArea = (form["filterArea"].FirstOrDefault() ?? "").Trim();
            var fLoc = (form["filterLocation"].FirstOrDefault() ?? "").Trim();
            var fMachine = (form["filterMachine"].FirstOrDefault() ?? "").Trim();
            var fConn = (form["filterConn"].FirstOrDefault() ?? "").Trim();
            var fStatus = (form["filterStatus"].FirstOrDefault() ?? "").Trim();

            var rows = await BuildDashboardRowsAsync();

            if (!string.IsNullOrEmpty(fRegion))
                rows = rows.Where(r => (r.Region ?? "") == fRegion).ToList();
            if (!string.IsNullOrEmpty(fArea))
                rows = rows.Where(r => (r.Area ?? "") == fArea).ToList();
            if (!string.IsNullOrEmpty(fLoc))
                rows = rows.Where(r => r.Location == fLoc ||
                    (r.StoreName ?? "").ToLower().Contains(fLoc.ToLower())).ToList();
            if (!string.IsNullOrEmpty(fMachine))
                rows = rows.Where(r =>
                    r.MachineId.ToString() == fMachine ||
                    (r.MachineName ?? "").ToLower().Contains(fMachine.ToLower())).ToList();
            if (!string.IsNullOrEmpty(fConn))
                rows = rows.Where(r => r.ConnectivityStatus == fConn).ToList();
            if (!string.IsNullOrEmpty(fStatus))
                rows = rows.Where(r => (r.LastSyncStatus ?? "") == fStatus).ToList();
            if (!string.IsNullOrEmpty(search))
                rows = rows.Where(r =>
                    (r.MachineName ?? "").ToLower().Contains(search) ||
                    (r.MachineIP ?? "").ToLower().Contains(search) ||
                    (r.StoreName ?? "").ToLower().Contains(search) ||
                    (r.Area ?? "").ToLower().Contains(search) ||
                    (r.Region ?? "").ToLower().Contains(search)).ToList();

            int total = rows.Count;
            var pageData = rows.Skip(start).Take(length).ToList();

            // Monitor stats over ALL matching rows (not just page)
            var stats = new ForceSyncMonitorStats
            {
                TotalSelected = total,
                Completed = rows.Count(r => r.LastSyncStatus == "Success"),
                InProgress = rows.Count(r => r.LastSyncStatus == "InProgress"),
                Failed = rows.Count(r => r.LastSyncStatus == "Failed"),
                Pending = rows.Count(r => r.HasPending)
            };

            return Json(new
            {
                draw,
                recordsTotal = total,
                recordsFiltered = total,
                stats,
                data = pageData.Select(r => new
                {
                    r.MachineId,
                    r.MachineName,
                    r.MachineIP,
                    r.Location,
                    r.StoreName,
                    r.Area,
                    r.Region,
                    r.ConnectivityStatus,
                    r.IsOnline,
                    lastSyncAt = r.LastSyncAt.HasValue
                        ? r.LastSyncAt.Value.ToString("dd-MMM-yyyy hh:mm tt") : "Never",
                    r.LastSyncLabel,
                    r.LastSyncStatus,
                    r.LastSyncType,
                    r.LastSyncDurationLabel,
                    r.LastRecordsFetched,
                    r.PendingForceSyncId,
                    r.PendingStatus,
                    r.HasPending
                })
            });
        }

        // ---------------------------------------------------------------
        //  POST /HR/ForceSync/QueueSync
        //  Writes Pending records to ForceSyncLogs for the service to pick up
        // ---------------------------------------------------------------
        [HttpPost]
        public async Task<IActionResult> QueueSync([FromBody] ForceSyncRequest request)
        {
            if (request == null || request.MachineIds == null || request.MachineIds.Count == 0)
                return Json(new ForceSyncQueueResult { Success = false, Message = "No machines selected." });

            string userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "Unknown";
            string userName = User.FindFirstValue(ClaimTypes.Name)
                           ?? User.Identity?.Name ?? "Unknown";

            var validMachines = await _db.AttendenceMachines
                .Where(m => request.MachineIds.Contains(m.Id) && m.IsActive)
                .Select(m => new { m.Id, m.IpAddress })
                .ToListAsync();

            if (validMachines.Count == 0)
                return Json(new ForceSyncQueueResult { Success = false, Message = "No valid active machines found." });

            // Cancel any existing Pending for same machines to avoid duplicate queue
            var existingPending = await _db.ForceSyncLogs
                .Where(l => request.MachineIds.Contains(l.MachineId) && l.Status == "Pending")
                .ToListAsync();

            foreach (var p in existingPending)
            {
                p.Status = "Failed";
                p.ErrorMessage = "Superseded by newer sync request.";
                p.CompletedAt = DateTime.Now;
            }

            var logs = validMachines.Select(m => new ForceSyncLog
            {
                MachineId = m.Id,
                MachineIP = m.IpAddress,
                SyncType = "Manual",
                Status = "Pending",
                RequestedAt = DateTime.Now,
                RequestedByUserId = userId,
                RequestedByUserName = userName,
                Notes = request.Notes
            }).ToList();

            _db.ForceSyncLogs.AddRange(logs);
            await _db.SaveChangesAsync();

            return Json(new ForceSyncQueueResult
            {
                Success = true,
                Queued = logs.Count,
                LogIds = logs.Select(l => l.Id).ToList(),
                Message = $"Sync queued for {logs.Count} machine(s). The service will execute immediately."
            });
        }

        // ---------------------------------------------------------------
        //  GET /HR/ForceSync/PollStatus?logIds=1,2,3
        //  Frontend polls this every 3 seconds to get live progress
        // ---------------------------------------------------------------
        [HttpGet]
        public async Task<IActionResult> PollStatus(string logIds)
        {
            if (string.IsNullOrEmpty(logIds))
                return Json(new List<object>());

            var ids = logIds.Split(',')
                .Select(x => int.TryParse(x, out var i) ? i : 0)
                .Where(i => i > 0).ToList();

            var statuses = await _db.ForceSyncLogs
                .Where(l => ids.Contains(l.Id))
                .Select(l => new
                {
                    l.Id,
                    l.MachineId,
                    l.Status,
                    l.RecordsFetched,
                    l.ErrorMessage,
                    l.DurationSeconds,
                    startedAt = l.StartedAt.HasValue
                        ? l.StartedAt.Value.ToString("hh:mm:ss tt") : null,
                    completedAt = l.CompletedAt.HasValue
                        ? l.CompletedAt.Value.ToString("hh:mm:ss tt") : null
                })
                .ToListAsync();

            return Json(statuses);
        }

        // ---------------------------------------------------------------
        //  GET /HR/ForceSync/GetSyncHistory?machineId=5
        // ---------------------------------------------------------------
        [HttpGet]
        public async Task<IActionResult> GetSyncHistory(int machineId)
        {
            var logs = await _db.ForceSyncLogs
                .Where(l => l.MachineId == machineId)
                .OrderByDescending(l => l.RequestedAt)
                .Take(20)
                .Select(l => new
                {
                    l.Id,
                    l.SyncType,
                    l.Status,
                    l.RecordsFetched,
                    l.DurationSeconds,
                    requestedAt = l.RequestedAt.ToString("dd-MMM-yyyy hh:mm tt"),
                    completedAt = l.CompletedAt.HasValue
                        ? l.CompletedAt.Value.ToString("dd-MMM-yyyy hh:mm tt") : "—",
                    l.RequestedByUserName,
                    l.ErrorMessage,
                    l.Notes
                })
                .ToListAsync();

            return Json(logs);
        }

        // ===============================================================
        //  PRIVATE: Build dashboard rows
        // ===============================================================
        private async Task<List<ForceSyncDashboardRow>> BuildDashboardRowsAsync()
        {
            DateTime onlineThreshold = DateTime.Now.AddMinutes(-OnlineThresholdMinutes);

            // Latest auto sync per machine (from AttendenceMachineConnectionLogs)
            var autoSyncs = await _db.AttendenceMachineConnectionLogs
                .GroupBy(l => l.MachineId)
                .Select(g => g.OrderByDescending(l => l.Id).FirstOrDefault())
                .ToListAsync();
            var autoDict = autoSyncs.Where(l => l != null)
                .ToDictionary(l => l.MachineId);

            // Latest force sync per machine (from ForceSyncLogs — any status)
            var forceSyncs = await _db.ForceSyncLogs
                .GroupBy(l => l.MachineId)
                .Select(g => g.OrderByDescending(l => l.RequestedAt).FirstOrDefault())
                .ToListAsync();
            var forceDict = forceSyncs.Where(l => l != null)
                .ToDictionary(l => l.MachineId);

            // Latest Pending force sync per machine
            var pendingSyncs = await _db.ForceSyncLogs
                .Where(l => l.Status == "Pending" || l.Status == "InProgress")
                .GroupBy(l => l.MachineId)
                .Select(g => g.OrderByDescending(l => l.RequestedAt).FirstOrDefault())
                .ToListAsync();
            var pendingDict = pendingSyncs.Where(l => l != null)
                .ToDictionary(l => l.MachineId);

            var machines = await _db.AttendenceMachines
                .Where(m => m.IsActive)
                .ToListAsync();

            var storeCodes = machines
                .Where(m => m.Location != null)
                .Select(m => m.Location).Distinct().ToList();

            var stores = await _db.Stores
                .Where(s => storeCodes.Contains(s.StoreCode))
                .ToListAsync();
            var storeDict = stores.ToDictionary(s => s.StoreCode);

            var rows = new List<ForceSyncDashboardRow>();

            foreach (var m in machines)
            {
                autoDict.TryGetValue(m.Id, out var auto);
                forceDict.TryGetValue(m.Id, out var force);
                pendingDict.TryGetValue(m.Id, out var pending);
                storeDict.TryGetValue(m.Location ?? "", out var store);

                bool online = auto != null
                    && auto.Status == "Success"
                    && auto.Connection_StartTime >= onlineThreshold;

                // Determine the most recent sync regardless of type
                DateTime? lastSyncAt = null;
                string lastStatus = "Never";
                string lastType = "—";
                int? lastDuration = null;
                int? lastRecords = null;

                bool autoIsNewer = auto != null
                    && (force == null || auto.Connection_StartTime > (force.CompletedAt ?? force.RequestedAt));

                if (autoIsNewer && auto != null)
                {
                    lastSyncAt = auto.Connection_StartTime;
                    lastStatus = auto.Status == "Success" ? "Success" : "Failed";
                    lastType = "Auto";
                    lastRecords = auto.RecordsRead;
                }
                else if (force != null)
                {
                    lastSyncAt = force.CompletedAt ?? force.RequestedAt;
                    lastStatus = force.Status;
                    lastType = "Manual";
                    lastDuration = force.DurationSeconds;
                    lastRecords = force.RecordsFetched;
                }

                string durationLabel = lastDuration.HasValue
                    ? (lastDuration.Value < 60
                        ? $"{lastDuration.Value}s"
                        : $"{lastDuration.Value / 60}m {lastDuration.Value % 60}s")
                    : "—";

                rows.Add(new ForceSyncDashboardRow
                {
                    MachineId = m.Id,
                    MachineName = m.Name,
                    MachineIP = m.IpAddress,
                    Port = m.Port,
                    Location = m.Location,
                    StoreName = store?.StoreName,
                    Area = store?.Area,
                    Region = store?.Region,
                    ConnectivityStatus = online ? "Online" : "Offline",
                    LastSyncAt = lastSyncAt,
                    LastSyncLabel = lastSyncAt.HasValue ? GetTimeAgo(lastSyncAt.Value) : "Never",
                    LastSyncStatus = lastStatus,
                    LastSyncType = lastType,
                    LastSyncDuration = lastDuration,
                    LastSyncDurationLabel = durationLabel,
                    LastRecordsFetched = lastRecords,
                    PendingForceSyncId = pending?.Id,
                    PendingStatus = pending?.Status
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