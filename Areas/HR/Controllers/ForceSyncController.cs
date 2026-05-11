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
        private readonly ILogger<ForceSyncController> _logger;
        public ForceSyncController(ServiceHubContext db, ILogger<ForceSyncController> logger)
        {
            _db = db;
            _logger = logger;
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
                    .Select(s => s.Area!.Name)
                    .Distinct().OrderBy(a => a).ToListAsync(),

                Regions = await _db.Stores
                    .Where(s => s.Region != null)
                    .Select(s => s.Region!.Name)
                    .Distinct().OrderBy(r => r).ToListAsync()
            };
            return View(vm);
        }

        // ---------------------------------------------------------------
        //  POST /HR/ForceSync/GetDashboardData
        //  Client-side DataTable: returns ALL rows in one call.
        //  Filtering and pagination are handled entirely in the browser.
        //  .ToList() on the projection ensures eager evaluation inside the
        //  try block — lazy LINQ eval after return would bypass the catch.
        // ---------------------------------------------------------------
        [HttpPost]
        public async Task<IActionResult> GetDashboardData()
        {
            try
            {
                var rows = await BuildDashboardRowsAsync();

                var stats = new ForceSyncMonitorStats
                {
                    TotalSelected = rows.Count,
                    Completed  = rows.Count(r => r.LastSyncStatus == "Success"),
                    InProgress = rows.Count(r => r.LastSyncStatus == "InProgress"),
                    Failed     = rows.Count(r => r.LastSyncStatus == "Failed"),
                    Pending    = rows.Count(r => r.HasPending)
                };

                // ToList() forces eager evaluation here so any projection
                // exception is caught below, not during response serialization.
                var data = rows.Select(r => new
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
                }).ToList();

                return Json(new { data, stats });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetDashboardData failed: {Error}", ex.ToString());
                // HTTP 200 so DataTables does not trigger its error callback loop.
                return Json(new
                {
                    data  = Array.Empty<object>(),
                    error = ex.Message,
                    stats = new { TotalSelected = 0, Completed = 0, InProgress = 0, Failed = 0, Pending = 0 }
                });
            }
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

            // (string?) casts tell EF Core to use the nullable reader — avoids
            // SqlNullValueException when DB columns are NULL but entity type is string.
            var raw = await _db.ForceSyncLogs
                .Where(l => ids.Contains(l.Id))
                .Select(l => new
                {
                    l.Id,
                    l.MachineId,
                    Status          = (string?)l.Status,
                    RecordsFetched  = (int?)l.RecordsFetched,
                    ErrorMessage    = (string?)l.ErrorMessage,
                    DurationSeconds = (int?)l.DurationSeconds,
                    l.StartedAt,
                    l.CompletedAt
                })
                .ToListAsync();

            // Date formatting done in memory — EF Core cannot translate ToString() to SQL.
            var statuses = raw.Select(l => new
            {
                l.Id,
                l.MachineId,
                status          = l.Status ?? "Pending",
                l.RecordsFetched,
                errorMessage    = l.ErrorMessage ?? "",
                l.DurationSeconds,
                startedAt   = l.StartedAt.HasValue   ? l.StartedAt.Value.ToString("hh:mm:ss tt")   : (string?)null,
                completedAt = l.CompletedAt.HasValue ? l.CompletedAt.Value.ToString("hh:mm:ss tt") : (string?)null
            }).ToList();

            return Json(statuses);
        }

        // ---------------------------------------------------------------
        //  GET /HR/ForceSync/GetSyncHistory?machineId=5
        // ---------------------------------------------------------------
        [HttpGet]
        public async Task<IActionResult> GetSyncHistory(int machineId)
        {
            // (string?) casts use the nullable DB reader — prevents SqlNullValueException
            // on columns that are NULL in the database but typed as string on the entity.
            var raw = await _db.ForceSyncLogs
                .Where(l => l.MachineId == machineId)
                .OrderByDescending(l => l.RequestedAt)
                .Take(20)
                .Select(l => new
                {
                    l.Id,
                    SyncType            = (string?)l.SyncType,
                    Status              = (string?)l.Status,
                    l.RecordsFetched,
                    l.DurationSeconds,
                    l.RequestedAt,
                    l.CompletedAt,
                    RequestedByUserName = (string?)l.RequestedByUserName,
                    ErrorMessage        = (string?)l.ErrorMessage,
                    Notes               = (string?)l.Notes
                })
                .ToListAsync();

            // Date formatting done in memory after materialization.
            var logs = raw.Select(l => new
            {
                l.Id,
                syncType            = l.SyncType ?? "Manual",
                status              = l.Status   ?? "—",
                recordsFetched      = l.RecordsFetched,
                durationSeconds     = l.DurationSeconds,
                requestedAt         = l.RequestedAt != default
                                        ? l.RequestedAt.ToString("dd-MMM-yyyy hh:mm tt") : "—",
                completedAt         = l.CompletedAt.HasValue
                                        ? l.CompletedAt.Value.ToString("dd-MMM-yyyy hh:mm tt") : "—",
                requestedByUserName = l.RequestedByUserName ?? "—",
                errorMessage        = l.ErrorMessage ?? "",
                notes               = l.Notes ?? ""
            }).ToList();

            return Json(logs);
        }

        // ---------------------------------------------------------------
        //  GET /HR/ForceSync/PingMachine?machineId=5
        //  Per-row reachability check — called by JS for live row-by-row updates.
        //  Three-layer check because IIS app pool cannot send raw ICMP:
        //    1) ICMP ping (works if IIS has raw-socket permission)
        //    2) TCP connect on ZKTeco port (works across most firewalls)
        //    3) DB fallback — recent successful Windows-Service sync proves reachable
        // ---------------------------------------------------------------
        [HttpGet]
        public async Task<IActionResult> PingMachine(int machineId)
        {
            var machine = await _db.AttendenceMachines
                .Where(m => m.Id == machineId && m.IsActive)
                .Select(m => new { m.Id, m.IpAddress, m.Port })
                .FirstOrDefaultAsync();

            if (machine == null)
                return Json(new { machineId, isOnline = false });

            bool online = await Task.Run(() => IsMachineReachable(machine.IpAddress, machine.Port));

            // Layer 3: DB fallback — Windows Service CAN reach the machine; if it
            // synced successfully in the last 24 h, treat the machine as Online.
            if (!online)
            {
                var cutoff = DateTime.Now.AddHours(-24);
                online = await _db.AttendenceMachineConnectionLogs
                    .AnyAsync(l => l.MachineId == machineId
                                && l.Status == "Success"
                                && l.Connection_StartTime >= cutoff);
            }

            return Json(new { machineId = machine.Id, isOnline = online });
        }

        // ---------------------------------------------------------------
        //  GET /HR/ForceSync/PingMachines
        //  Bulk reachability check for all active machines in parallel.
        // ---------------------------------------------------------------
        [HttpGet]
        public async Task<IActionResult> PingMachines()
        {
            var machines = await _db.AttendenceMachines
                .Where(m => m.IsActive)
                .Select(m => new { m.Id, m.IpAddress, m.Port })
                .ToListAsync();

            var tasks = machines.Select(m => Task.Run(() =>
            {
                bool online = IsMachineReachable(m.IpAddress, m.Port);
                return new { machineId = m.Id, isOnline = online };
            }));

            var results = await Task.WhenAll(tasks);
            return Json(results);
        }

        // Three-layer reachability check:
        //   1) ICMP ping — fastest, but IIS app pool may lack raw-socket privilege.
        //   2) TCP connect on ZKTeco port — reliable fallback, no special privilege.
        // Layer 3 (DB fallback) is applied only in PingMachine where _db is available.
        private static bool IsMachineReachable(string ip, int port = 4370)
        {
            // Layer 1: ICMP
            try
            {
                using var icmp = new System.Net.NetworkInformation.Ping();
                var reply = icmp.Send(ip, 2000);
                if (reply?.Status == System.Net.NetworkInformation.IPStatus.Success)
                    return true;
            }
            catch { }

            // Layer 2: TCP connect on ZKTeco port
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

        // ===============================================================
        //  PRIVATE: Build dashboard rows
        //  Projects DB queries to named record types with nullable DateTime
        //  to avoid SqlNullValueException on legacy NULL rows.
        // ===============================================================
        private async Task<List<ForceSyncDashboardRow>> BuildDashboardRowsAsync()
        {

            // ── Auto sync: latest per machine ────────────────────────────
            var autoMaxIds = await _db.AttendenceMachineConnectionLogs
                .GroupBy(l => l.MachineId)
                .Select(g => g.Max(l => l.Id))
                .ToListAsync();

            // Named record projection — DateTime? prevents SqlNullValueException on legacy NULLs
            List<AutoSyncRow> autoList;
            if (autoMaxIds.Count > 0)
            {
                autoList = await _db.AttendenceMachineConnectionLogs
                    .Where(l => autoMaxIds.Contains(l.Id))
                    .Select(l => new AutoSyncRow
                    {
                        Id = l.Id,
                        MachineId = l.MachineId,
                        Status = l.Status,
                        RecordsRead = l.RecordsRead,
                        StartTime = (DateTime?)l.Connection_StartTime
                    })
                    .ToListAsync();
            }
            else autoList = new List<AutoSyncRow>();

            var autoDict = autoList
                .GroupBy(x => x.MachineId)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.Id).First());

            // ── Force sync: latest per machine (any status) ──────────────
            var forceMaxIds = await _db.ForceSyncLogs
                .GroupBy(l => l.MachineId)
                .Select(g => g.Max(l => l.Id))
                .ToListAsync();

            List<ForceSyncRow> forceList;
            if (forceMaxIds.Count > 0)
            {
                forceList = await _db.ForceSyncLogs
                    .Where(l => forceMaxIds.Contains(l.Id))
                    .Select(l => new ForceSyncRow
                    {
                        Id = l.Id,
                        MachineId = l.MachineId,
                        Status = l.Status,
                        RequestedAt = (DateTime?)l.RequestedAt,
                        CompletedAt = l.CompletedAt,
                        DurationSeconds = l.DurationSeconds,
                        RecordsFetched = l.RecordsFetched
                    })
                    .ToListAsync();
            }
            else forceList = new List<ForceSyncRow>();

            var forceDict = forceList
                .GroupBy(x => x.MachineId)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.Id).First());

            // ── Pending/InProgress force sync: latest per machine ────────
            var pendingMaxIds = await _db.ForceSyncLogs
                .Where(l => l.Status == "Pending" || l.Status == "InProgress")
                .GroupBy(l => l.MachineId)
                .Select(g => g.Max(l => l.Id))
                .ToListAsync();

            List<PendingSyncRow> pendingList;
            if (pendingMaxIds.Count > 0)
            {
                pendingList = await _db.ForceSyncLogs
                    .Where(l => pendingMaxIds.Contains(l.Id))
                    .Select(l => new PendingSyncRow { Id = l.Id, MachineId = l.MachineId, Status = l.Status })
                    .ToListAsync();
            }
            else pendingList = new List<PendingSyncRow>();

            var pendingDict = pendingList
                .GroupBy(x => x.MachineId)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.Id).First());

            // ── Machines & stores ─────────────────────────────────────────
            var machines = await _db.AttendenceMachines
                .Where(m => m.IsActive)
                .ToListAsync();

            var storeCodes = machines
                .Where(m => !string.IsNullOrEmpty(m.Location))
                .Select(m => m.Location).Distinct().ToList();

            var stores = storeCodes.Count > 0
                ? await _db.Stores.Where(s => storeCodes.Contains(s.StoreCode)).ToListAsync()
                : new List<Store>();

            var storeDict = stores
                .Where(s => !string.IsNullOrEmpty(s.StoreCode))
                .GroupBy(s => s.StoreCode)
                .ToDictionary(g => g.Key, g => g.First());

            // ── Build rows ───────────────────────────────────────────────
            // ConnectivityStatus is set from DB auto-sync log here; the JS
            // refreshPing() call updates it to live ICMP status asynchronously.
            DateTime onlineThreshold = DateTime.Now.AddMinutes(-70);
            var rows = new List<ForceSyncDashboardRow>();

            foreach (var m in machines)
            {
                autoDict.TryGetValue(m.Id, out var auto);
                forceDict.TryGetValue(m.Id, out var force);
                pendingDict.TryGetValue(m.Id, out var pending);
                storeDict.TryGetValue(m.Location ?? "", out var store);

                bool online = auto != null
                    && auto.Status == "Success"
                    && auto.StartTime.HasValue
                    && auto.StartTime.Value >= onlineThreshold;

                DateTime? lastSyncAt = null;
                string lastStatus = "Never";
                string lastType = "—";
                int? lastDuration = null;
                int? lastRecords = null;

                bool autoIsNewer = auto != null
                    && (force == null
                        || auto.StartTime > (force.CompletedAt ?? force.RequestedAt));

                if (autoIsNewer && auto != null)
                {
                    lastSyncAt = auto.StartTime;
                    lastStatus = auto.Status == "Success" ? "Success" : "Failed";
                    lastType = "Auto";
                    lastRecords = auto.RecordsRead;
                }
                else if (force != null)
                {
                    lastSyncAt = force.CompletedAt ?? force.RequestedAt;
                    lastStatus = force.Status ?? "—";
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
                    Area = store?.Area?.Name,
                    Region = store?.Region?.Name,
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

        // Projection records for DB queries.
        // DateTime? fields prevent SqlNullValueException on legacy NULL DB rows.
        private class AutoSyncRow  { public int Id { get; set; } public int MachineId { get; set; } public string Status { get; set; } public int? RecordsRead { get; set; } public DateTime? StartTime { get; set; } }
        private class ForceSyncRow { public int Id { get; set; } public int MachineId { get; set; } public string Status { get; set; } public DateTime? RequestedAt { get; set; } public DateTime? CompletedAt { get; set; } public int? DurationSeconds { get; set; } public int? RecordsFetched { get; set; } }
        private class PendingSyncRow { public int Id { get; set; } public int MachineId { get; set; } public string Status { get; set; } }
    }
}