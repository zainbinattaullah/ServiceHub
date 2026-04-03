using ClosedXML.Excel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ServiceHub.Data;
using ServiceHub.Areas.HR.Models;

namespace ServiceHub.Areas.HR.Controllers
{
    [Area("HR")]
    [Authorize]
    public class EnrollmentController : Controller
    {
        private readonly ServiceHubContext _dbContext;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<EnrollmentController> _logger;
        private readonly ServiceHub.Controllers.TimeWindowService _timeWindowService;

        public EnrollmentController(ServiceHubContext dbContext, IHttpClientFactory httpClientFactory, ILogger<EnrollmentController> logger, ServiceHub.Controllers.TimeWindowService timeWindowService)
        {
            _dbContext = dbContext;
            _httpClientFactory = httpClientFactory;
            _logger = logger;
            _timeWindowService = timeWindowService;
        }

        public async Task<IActionResult> Index()
        {
            var list = await _dbContext.EmployeeEnrollments
                .AsNoTracking()
                .OrderByDescending(e => e.CreatedAt)
                .Select(e => new {
                    e.Id,
                    e.EmployeeCode,
                    e.EmployeeName,
                    e.MachineIP,
                    e.Privilege,
                    e.IsSynced,
                    e.SyncMessage,
                    e.SyncedAt,
                    e.CreatedAt
                }).ToListAsync();

            ViewBag.IsTransferWindowOpen = _timeWindowService.IsTransferWindowOpen();
            ViewBag.TransferWindowMessage = _timeWindowService.GetTransferWindowMessage();
            ViewBag.NextWindowChange = _timeWindowService.GetNextWindowChange()?.TotalMilliseconds;
            return View(list);
        }

        [HttpPost]
        public async Task<IActionResult> GetEnrollments()
        {
            var request = HttpContext.Request.Form;
            var draw = request["draw"].FirstOrDefault();
            var start = request["start"].FirstOrDefault();
            var length = request["length"].FirstOrDefault();
            var searchValue = request["search[value]"].FirstOrDefault();
            var sortColumnIndex = request["order[0][column]"].FirstOrDefault();
            var sortDirection = request["order[0][dir]"].FirstOrDefault();

            int pageSize = length != null ? Convert.ToInt32(length) : 10;
            int skip = start != null ? Convert.ToInt32(start) : 0;

            var query = _dbContext.EmployeeEnrollments.AsQueryable();

            if (!string.IsNullOrEmpty(searchValue))
            {
                var sv = searchValue.ToLower();
                query = query.Where(e => e.EmployeeCode.Contains(sv) || (e.EmployeeName ?? "").Contains(sv) || (e.MachineIP ?? "").Contains(sv) || (e.Privilege ?? "").Contains(sv));
            }

            // Apply sorting
            if (!string.IsNullOrEmpty(sortColumnIndex))
            {
                switch (sortColumnIndex)
                {
                    case "0": query = sortDirection == "asc" ? query.OrderBy(e => e.Id) : query.OrderByDescending(e => e.Id); break;
                    case "1": query = sortDirection == "asc" ? query.OrderBy(e => e.EmployeeCode) : query.OrderByDescending(e => e.EmployeeCode); break;
                    case "2": query = sortDirection == "asc" ? query.OrderBy(e => e.EmployeeName) : query.OrderByDescending(e => e.EmployeeName); break;
                    case "3": query = sortDirection == "asc" ? query.OrderBy(e => e.MachineIP) : query.OrderByDescending(e => e.MachineIP); break;
                    case "4": query = sortDirection == "asc" ? query.OrderBy(e => e.Privilege) : query.OrderByDescending(e => e.Privilege); break;
                    case "5": query = sortDirection == "asc" ? query.OrderBy(e => e.CreatedAt) : query.OrderByDescending(e => e.CreatedAt); break;
                    case "6": query = sortDirection == "asc" ? query.OrderBy(e => e.IsSynced) : query.OrderByDescending(e => e.IsSynced); break;
                    default: query = query.OrderByDescending(e => e.CreatedAt); break;
                }
            }

            var totalRecords = await query.CountAsync();

            var data = await query.Skip(skip).Take(pageSize).Select(e => new {
                id = e.Id,
                employeeCode = e.EmployeeCode,
                employeeName = e.EmployeeName,
                machineIP = e.MachineIP,
                privilege = e.Privilege,
                createdAt = e.CreatedAt,
                isSynced = e.IsSynced,
                syncMessage = e.SyncMessage
            }).ToListAsync();

            return Json(new { draw = draw, recordsTotal = totalRecords, recordsFiltered = totalRecords, data = data });
        }

        [HttpGet]
        public async Task<IActionResult> ExportEnrollments(string search = null, string sortColumn = null, string sortDirection = null)
        {
            var query = _dbContext.EmployeeEnrollments.AsQueryable();
            if (!string.IsNullOrEmpty(search))
            {
                var sv = search.ToLower();
                query = query.Where(e => e.EmployeeCode.Contains(sv) || (e.EmployeeName ?? "").Contains(sv) || (e.MachineIP ?? "").Contains(sv) || (e.Privilege ?? "").Contains(sv));
            }

            if (!string.IsNullOrEmpty(sortColumn))
            {
                switch (sortColumn)
                {
                    case "1": query = sortDirection == "asc" ? query.OrderBy(e => e.EmployeeCode) : query.OrderByDescending(e => e.EmployeeCode); break;
                    case "2": query = sortDirection == "asc" ? query.OrderBy(e => e.EmployeeName) : query.OrderByDescending(e => e.EmployeeName); break;
                    case "3": query = sortDirection == "asc" ? query.OrderBy(e => e.MachineIP) : query.OrderByDescending(e => e.MachineIP); break;
                    case "4": query = sortDirection == "asc" ? query.OrderBy(e => e.Privilege) : query.OrderByDescending(e => e.Privilege); break;
                    case "5": query = sortDirection == "asc" ? query.OrderBy(e => e.CreatedAt) : query.OrderByDescending(e => e.CreatedAt); break;
                    case "6": query = sortDirection == "asc" ? query.OrderBy(e => e.IsSynced) : query.OrderByDescending(e => e.IsSynced); break;
                }
            }

            var records = await query.Select(e => new {
                e.Id,
                e.EmployeeCode,
                e.EmployeeName,
                e.MachineIP,
                e.Privilege,
                CreatedAt = e.CreatedAt,
                IsSynced = e.IsSynced ? "Yes" : "No",
                SyncMessage = e.SyncMessage
            }).ToListAsync();

            using (var wb = new XLWorkbook())
            {
                var ws = wb.Worksheets.Add("Enrollments");
                ws.Cell(1, 1).Value = "ID";
                ws.Cell(1, 2).Value = "Employee Code";
                ws.Cell(1, 3).Value = "Employee Name";
                ws.Cell(1, 4).Value = "Machine IP";
                ws.Cell(1, 5).Value = "Privilege";
                ws.Cell(1, 6).Value = "Created At";
                ws.Cell(1, 7).Value = "Is Synced";
                ws.Cell(1, 8).Value = "Sync Message";

                var r = 2;
                foreach (var rec in records)
                {
                    ws.Cell(r, 1).Value = rec.Id;
                    ws.Cell(r, 2).Value = rec.EmployeeCode;
                    ws.Cell(r, 3).Value = rec.EmployeeName;
                    ws.Cell(r, 4).Value = rec.MachineIP;
                    ws.Cell(r, 5).Value = rec.Privilege;
                    ws.Cell(r, 6).Value = rec.CreatedAt;
                    ws.Cell(r, 7).Value = rec.IsSynced;
                    ws.Cell(r, 8).Value = rec.SyncMessage;
                    r++;
                }

                using (var ms = new System.IO.MemoryStream())
                {
                    wb.SaveAs(ms);
                    var content = ms.ToArray();
                    return File(content, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "Enrollments.xlsx");
                }
            }
        }

        [HttpPost]
        public async Task<IActionResult> RetrySend(int id)
        {
            // Only allow retry during transfer window
            if (!_timeWindowService.IsTransferWindowOpen())
            {
                return StatusCode(403, "Transfer window is closed");
            }
            var enroll = await _dbContext.EmployeeEnrollments.FindAsync(id);
            if (enroll == null) return NotFound();

            if (enroll.IsSynced)
            {
                return BadRequest("Already synced");
            }

            try
            {
                var client = _httpClientFactory.CreateClient("EmployeeApi");
                var payload = new
                {
                    EmployeeCode = enroll.EmployeeCode,
                    EmployeeName = enroll.EmployeeName,
                    MachineIP = enroll.MachineIP,
                    Privilege = enroll.Privilege
                };

                HttpResponseMessage response;
                string responseString = null;
                try
                {
                    // ensure the client BaseAddress is configured (in Program.cs) and the relative path is correct
                    response = await client.PostAsJsonAsync("api/employee/register", payload);
                    responseString = await response.Content.ReadAsStringAsync();
                }
                catch (Exception httpEx)
                {
                    _logger.LogError(httpEx, "HTTP request to Employee API failed for enrollment {Id}", id);
                    return StatusCode(502, "Failed to contact Employee API");
                }

                bool success = response.IsSuccessStatusCode;
                string message = null;

                if (success)
                {
                    try
                    {
                        using var doc = System.Text.Json.JsonDocument.Parse(responseString ?? string.Empty);
                        var root = doc.RootElement;
                        if (root.TryGetProperty("success", out var succ) && succ.ValueKind == System.Text.Json.JsonValueKind.True)
                            success = true;
                        if (root.TryGetProperty("message", out var msg) && msg.ValueKind == System.Text.Json.JsonValueKind.String)
                            message = msg.GetString();
                    }
                    catch { /* ignore parse errors and keep raw response */ }
                }
                else
                {
                    // Log 404 or other non-success responses to help debugging
                    _logger.LogWarning("Employee API returned {Status} {Reason} for enrollment {Id}: {Content}", (int)response.StatusCode, response.ReasonPhrase, responseString);
                    message = responseString;
                }

                enroll.IsSynced = success;
                enroll.SyncMessage = message ?? (success ? "OK" : responseString ?? "");
                enroll.SyncedAt = success ? DateTime.UtcNow : (DateTime?)null;
                _dbContext.EmployeeEnrollments.Update(enroll);
                await _dbContext.SaveChangesAsync();

                return Json(new { success = success, message = enroll.SyncMessage });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "RetrySend failed for enrollment {Id}", id);
                return StatusCode(500, "Error while sending to service");
            }
        }
    }
}
