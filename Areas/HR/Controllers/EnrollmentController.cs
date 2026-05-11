using ClosedXML.Excel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ServiceHub.Data;
using ServiceHub.Areas.HR.Models;
using System.Security.Claims;

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

            var query = _dbContext.EmployeeEnrollments
                .GroupJoin(
                    _dbContext.Departments,
                    e => e.DepartmentId,
                    d => d.Id,
                    (e, depts) => new { Enrollment = e, Dept = depts.FirstOrDefault() }
                )
                .AsQueryable();

            if (!string.IsNullOrEmpty(searchValue))
            {
                var sv = searchValue.ToLower();
                query = query.Where(x =>
                    x.Enrollment.EmployeeCode.Contains(sv) ||
                    (x.Enrollment.EmployeeName ?? "").Contains(sv) ||
                    (x.Enrollment.MachineIP ?? "").Contains(sv) ||
                    (x.Enrollment.Privilege ?? "").Contains(sv) ||
                    (x.Dept != null && (x.Dept.Code + " " + x.Dept.Name).ToLower().Contains(sv))
                );
            }

            if (!string.IsNullOrEmpty(sortColumnIndex))
            {
                switch (sortColumnIndex)
                {
                    case "0": query = sortDirection == "asc" ? query.OrderBy(x => x.Enrollment.Id) : query.OrderByDescending(x => x.Enrollment.Id); break;
                    case "1": query = sortDirection == "asc" ? query.OrderBy(x => x.Enrollment.EmployeeCode) : query.OrderByDescending(x => x.Enrollment.EmployeeCode); break;
                    case "2": query = sortDirection == "asc" ? query.OrderBy(x => x.Enrollment.EmployeeName) : query.OrderByDescending(x => x.Enrollment.EmployeeName); break;
                    case "3": query = sortDirection == "asc" ? query.OrderBy(x => x.Enrollment.MachineIP) : query.OrderByDescending(x => x.Enrollment.MachineIP); break;
                    case "4": query = sortDirection == "asc" ? query.OrderBy(x => x.Enrollment.Privilege) : query.OrderByDescending(x => x.Enrollment.Privilege); break;
                    case "5": query = sortDirection == "asc" ? query.OrderBy(x => x.Enrollment.CreatedAt) : query.OrderByDescending(x => x.Enrollment.CreatedAt); break;
                    case "6": query = sortDirection == "asc" ? query.OrderBy(x => x.Enrollment.IsSynced) : query.OrderByDescending(x => x.Enrollment.IsSynced); break;
                    case "7": query = sortDirection == "asc" ? query.OrderBy(x => x.Dept != null ? x.Dept.Name : "") : query.OrderByDescending(x => x.Dept != null ? x.Dept.Name : ""); break;
                    default: query = query.OrderByDescending(x => x.Enrollment.CreatedAt); break;
                }
            }
            else
            {
                query = query.OrderByDescending(x => x.Enrollment.CreatedAt);
            }

            var totalRecords = await query.CountAsync();

            var data = await query.Skip(skip).Take(pageSize).Select(x => new {
                id = x.Enrollment.Id,
                employeeCode = x.Enrollment.EmployeeCode,
                employeeName = x.Enrollment.EmployeeName,
                machineIP = x.Enrollment.MachineIP,
                privilege = x.Enrollment.Privilege,
                createdAt = x.Enrollment.CreatedAt,
                isSynced = x.Enrollment.IsSynced,
                syncMessage = x.Enrollment.SyncMessage,
                isActive = x.Enrollment.IsActive,
                department = x.Dept != null ? x.Dept.Code + " — " + x.Dept.Name : ""
            }).ToListAsync();

            return Json(new { draw, recordsTotal = totalRecords, recordsFiltered = totalRecords, data });
        }

        [HttpGet]
        public async Task<IActionResult> ExportEnrollments(string search = null, string sortColumn = null, string sortDirection = null)
        {
            var query = _dbContext.EmployeeEnrollments
                .GroupJoin(
                    _dbContext.Departments,
                    e => e.DepartmentId,
                    d => d.Id,
                    (e, depts) => new { Enrollment = e, Dept = depts.FirstOrDefault() }
                )
                .AsQueryable();

            if (!string.IsNullOrEmpty(search))
            {
                var sv = search.ToLower();
                query = query.Where(x =>
                    x.Enrollment.EmployeeCode.Contains(sv) ||
                    (x.Enrollment.EmployeeName ?? "").Contains(sv) ||
                    (x.Enrollment.MachineIP ?? "").Contains(sv) ||
                    (x.Enrollment.Privilege ?? "").Contains(sv)
                );
            }

            if (!string.IsNullOrEmpty(sortColumn))
            {
                switch (sortColumn)
                {
                    case "1": query = sortDirection == "asc" ? query.OrderBy(x => x.Enrollment.EmployeeCode) : query.OrderByDescending(x => x.Enrollment.EmployeeCode); break;
                    case "2": query = sortDirection == "asc" ? query.OrderBy(x => x.Enrollment.EmployeeName) : query.OrderByDescending(x => x.Enrollment.EmployeeName); break;
                    case "3": query = sortDirection == "asc" ? query.OrderBy(x => x.Enrollment.MachineIP) : query.OrderByDescending(x => x.Enrollment.MachineIP); break;
                    case "4": query = sortDirection == "asc" ? query.OrderBy(x => x.Enrollment.Privilege) : query.OrderByDescending(x => x.Enrollment.Privilege); break;
                    case "5": query = sortDirection == "asc" ? query.OrderBy(x => x.Enrollment.CreatedAt) : query.OrderByDescending(x => x.Enrollment.CreatedAt); break;
                    case "6": query = sortDirection == "asc" ? query.OrderBy(x => x.Enrollment.IsSynced) : query.OrderByDescending(x => x.Enrollment.IsSynced); break;
                    case "7": query = sortDirection == "asc" ? query.OrderBy(x => x.Dept != null ? x.Dept.Name : "") : query.OrderByDescending(x => x.Dept != null ? x.Dept.Name : ""); break;
                }
            }

            var records = await query.Select(x => new {
                x.Enrollment.Id,
                x.Enrollment.EmployeeCode,
                x.Enrollment.EmployeeName,
                x.Enrollment.MachineIP,
                x.Enrollment.Privilege,
                x.Enrollment.CreatedAt,
                IsSynced = x.Enrollment.IsSynced ? "Yes" : "No",
                x.Enrollment.SyncMessage,
                Department = x.Dept != null ? x.Dept.Code + " — " + x.Dept.Name : ""
            }).ToListAsync();

            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add("Enrollments");

            // Headers
            ws.Cell(1, 1).Value = "ID";
            ws.Cell(1, 2).Value = "Employee Code";
            ws.Cell(1, 3).Value = "Employee Name";
            ws.Cell(1, 4).Value = "Department";
            ws.Cell(1, 5).Value = "Machine IP";
            ws.Cell(1, 6).Value = "Privilege";
            ws.Cell(1, 7).Value = "Created At";
            ws.Cell(1, 8).Value = "Is Synced";
            ws.Cell(1, 9).Value = "Sync Message";

            var r = 2;
            foreach (var rec in records)
            {
                ws.Cell(r, 1).Value = rec.Id;
                ws.Cell(r, 2).Value = rec.EmployeeCode;
                ws.Cell(r, 3).Value = rec.EmployeeName;
                ws.Cell(r, 4).Value = rec.Department;
                ws.Cell(r, 5).Value = rec.MachineIP;
                ws.Cell(r, 6).Value = rec.Privilege;
                ws.Cell(r, 7).Value = rec.CreatedAt;
                ws.Cell(r, 8).Value = rec.IsSynced;
                ws.Cell(r, 9).Value = rec.SyncMessage;
                r++;
            }

            ws.Columns().AdjustToContents();

            using var ms = new System.IO.MemoryStream();
            wb.SaveAs(ms);
            return File(ms.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "Enrollments.xlsx");
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
                int privilegeLevel = (enroll.Privilege ?? "").ToLower() switch
                {
                    "superadmin" => 14,
                    _ => 0   // "User" and anything else → regular user
                };
                var payload = new
                {
                    EmployeeCode = enroll.EmployeeCode,
                    EmployeeName = enroll.EmployeeName,
                    MachineIP = enroll.MachineIP,
                    Privilege = privilegeLevel
                };

                HttpResponseMessage response;
                string responseString = null;
                try
                {
                    response = await client.PostAsJsonAsync("api/employees/register/", payload);
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

        // ── POST: ToggleActive — flip IsActive for all enrollments of an employee
        //         and queue a device command so the Windows Service updates ZKTeco.
        [HttpPost]
        public async Task<IActionResult> ToggleActive([FromBody] ToggleActiveRequest req)
        {
            if (req == null || string.IsNullOrWhiteSpace(req.EmployeeCode))
                return Json(new { success = false, message = "Employee code required." });

            var enrollments = await _dbContext.EmployeeEnrollments
                .Where(e => e.EmployeeCode == req.EmployeeCode)
                .ToListAsync();

            if (!enrollments.Any())
                return Json(new { success = false, message = "Employee not found." });

            bool newState = !enrollments.Any(e => e.IsActive);
            string action = newState ? "Activate" : "Deactivate";

            foreach (var e in enrollments)
                e.IsActive = newState;

            // Mirror in biometric log table
            await _dbContext.Database.ExecuteSqlRawAsync(
                "UPDATE Employee_Biometric_Log SET IsActive = {0}, LastUpdated = GETDATE() WHERE Emp_No = {1}",
                newState ? 1 : 0, req.EmployeeCode);

            string userId   = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "System";
            string userName = User.FindFirstValue(ClaimTypes.Name) ?? User.Identity?.Name ?? "System";

            _dbContext.EmployeeDeviceCommands.Add(new EmployeeDeviceCommand
            {
                EmployeeCode        = req.EmployeeCode,
                EmployeeName        = enrollments.First().EmployeeName,
                Action              = action,
                Status              = "Pending",
                RequestedAt         = DateTime.Now,
                RequestedByUserId   = userId,
                RequestedByUserName = userName
            });

            await _dbContext.SaveChangesAsync();

            return Json(new { success = true, isActive = newState, action });
        }

        public class ToggleActiveRequest
        {
            public string EmployeeCode { get; set; }
        }
    }
}
