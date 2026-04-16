using ClosedXML.Excel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ServiceHub.Areas.HR.Models;
using ServiceHub.Data;
using System.Security.Claims;

namespace ServiceHub.Areas.HR.Controllers
{
    [Area("HR")]
    [Authorize]
    public class AttendanceMachineController : Controller
    {
        private readonly ServiceHubContext _dbcontext;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<AttendanceMachineController> _logger;

        public AttendanceMachineController(ServiceHubContext context,
            IHttpClientFactory httpClientFactory,
            ILogger<AttendanceMachineController> logger)
        {
            _dbcontext = context;
            _httpClientFactory = httpClientFactory;
            _logger = logger;
        }

        public async Task<IActionResult> Index()
        {
            return View(await _dbcontext.AttendenceMachines.ToListAsync());
        }

        [HttpPost]
        public async Task<IActionResult> GetAttendanceMachines()
        {
            var request = HttpContext.Request.Form;
            // DataTables parameters
            var draw = request["draw"].FirstOrDefault();
            var start = request["start"].FirstOrDefault();
            var length = request["length"].FirstOrDefault();
            var searchValue = request["search[value]"].FirstOrDefault();
            var sortColumn = request["columns[" + request["order[0][column]"].FirstOrDefault() + "][name]"].FirstOrDefault();
            var sortDirection = request["order[0][dir]"].FirstOrDefault();

            int pageSize = length != null ? Convert.ToInt32(length) : 10;
            int skip = start != null ? Convert.ToInt32(start) : 0;

            // Query data from the database
            var query = _dbcontext.AttendenceMachines.AsQueryable();

            // Apply search filter
            if (!string.IsNullOrEmpty(searchValue))
            {
                var search = searchValue.ToLower();

                query = query.Where(m =>
                    m.Name.Contains(searchValue) ||
                    m.IpAddress.Contains(searchValue) ||
                    (m.Description ?? "").Contains(searchValue) ||
                    (m.Location ?? "").Contains(searchValue) ||
                    (search == "yes" && m.IsActive) ||
                    (search == "no" && !m.IsActive) ||
                    (search == "all" && m.IsFetchAll) ||
                    (search == "latest" && !m.IsFetchAll)
                );
            }

            // Apply sorting
            if (!string.IsNullOrEmpty(sortColumn))
            {
                switch (sortColumn)
                {
                    case "name":
                        query = sortDirection == "asc" ? query.OrderBy(m => m.Name) : query.OrderByDescending(m => m.Name);
                        break;
                    case "ipAddress":
                        query = sortDirection == "asc" ? query.OrderBy(m => m.IpAddress) : query.OrderByDescending(m => m.IpAddress);
                        break;
                    case "port":
                        query = sortDirection == "asc" ? query.OrderBy(m => m.Port) : query.OrderByDescending(m => m.Port);
                        break;
                    case "description":
                        query = sortDirection == "asc" ? query.OrderBy(m => m.Description) : query.OrderByDescending(m => m.Description);
                        break;
                    case "location":
                        query = sortDirection == "asc" ? query.OrderBy(m => m.Location) : query.OrderByDescending(m => m.Location);
                        break;
                    case "isActive":
                        query = sortDirection == "asc" ? query.OrderBy(m => m.IsActive) : query.OrderByDescending(m => m.IsActive);
                        break;
                    case "isFetchAll":
                        query = sortDirection == "asc" ? query.OrderBy(m => m.IsFetchAll) : query.OrderByDescending(m => m.IsFetchAll);
                        break;
                }
            }

            // Get total records count
            int totalRecords = await query.CountAsync();


            // Paginate data
            var data = await query
                .Skip(skip)
                .Take(pageSize)
                .Select(m => new
                {
                    id = m.Id,
                    name = m.Name,
                    ipAddress = m.IpAddress,
                    port = m.Port,
                    description = m.Description,
                    location = m.Location,
                    isActive = m.IsActive,
                    isFetchAll = m.IsFetchAll
                })
                .ToListAsync();

            // Return JSON response
            return Json(new
            {
                draw = draw,
                recordsTotal = totalRecords,
                recordsFiltered = totalRecords,
                data = data
            });
        }

        public IActionResult Create()
        {
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("Id,Name,IpAddress,Port,IsActive,IsFetchAll,Location,Description,DeviceModel,CreatedAt,LastUpdated")] AttendanceMachine attendanceMachine)
        {           

            // Guard against null model binding
            if (attendanceMachine == null)
            {
                return BadRequest();
            }

            if (ModelState.IsValid)
            {
                if (attendanceMachine.Port < 1 || attendanceMachine.Port > 65535)
                {
                    ModelState.AddModelError("Port", "Port must be between 1 and 65535.");
                    return View(attendanceMachine);
                }
                attendanceMachine.CreatedAt = DateTime.UtcNow;
                attendanceMachine.IsActive = attendanceMachine.IsActive; 
                attendanceMachine.IsFetchAll = attendanceMachine.IsFetchAll;
                _dbcontext.AttendenceMachines.Add(attendanceMachine);
                await _dbcontext.SaveChangesAsync();
                return RedirectToAction(nameof(Index));
            }
            return View(attendanceMachine);
        }

        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }
            var attendanceMachine = await _dbcontext.AttendenceMachines.FindAsync(id);
            if (attendanceMachine == null)
            {
                return NotFound();
            }
            return View(attendanceMachine);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, [Bind("Id,Name,IpAddress,Port,IsActive,IsFetchAll,Location,Description,DeviceModel,CreatedAt,LastUpdated")] AttendanceMachine attendanceMachine)
        {
            // Guard against null model binding
            if (attendanceMachine == null)
            {
                return NotFound();
            }

            if (id != attendanceMachine.Id)
            {
                return NotFound();
            }
            if (ModelState.IsValid)
            {
                try
                {
                    if (attendanceMachine.Port < 1 || attendanceMachine.Port > 65535)
                    {
                        ModelState.AddModelError("Port", "Port must be between 1 and 65535.");
                        return View(attendanceMachine);
                    }
                    attendanceMachine.IsActive = attendanceMachine.IsActive;
                    attendanceMachine.IsFetchAll = attendanceMachine.IsFetchAll;
                    attendanceMachine.LastUpdated = DateTime.UtcNow;
                    _dbcontext.Update(attendanceMachine);
                    await _dbcontext.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!AttendanceMachineExists(attendanceMachine.Id))
                    {
                        return NotFound();
                    }
                    else
                    {
                        throw;
                    }
                }
                return RedirectToAction(nameof(Index));
            }
            return View(attendanceMachine);
        }

        private bool AttendanceMachineExists(int id)
        {
            return _dbcontext.AttendenceMachines.Any(e => e.Id == id);
        }


        // ---------------------------------------------------------------
        //  BULK FORMAT MACHINES  (Admin only)
        // ---------------------------------------------------------------
        [HttpPost]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> FormatMachines([FromBody] BulkFormatRequest request)
        {
            if (request == null || request.Machines == null || !request.Machines.Any())
                return BadRequest(new { success = false, message = "No machines provided." });

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
            var userName = User.Identity?.Name ?? "";

            var results = new List<object>();

            foreach (var item in request.Machines)
            {
                if (string.IsNullOrWhiteSpace(item.MachineIP))
                    continue;

                var machine = await _dbcontext.AttendenceMachines
                    .FirstOrDefaultAsync(m => m.IpAddress == item.MachineIP);

                string machineName = machine?.Name ?? item.MachineIP;
                bool success = false;
                string message = "";

                try
                {
                    var client = _httpClientFactory.CreateClient("EmployeeApi");
                    var payload = new { MachineIP = item.MachineIP, UserId = userId, UserName = userName };

                    HttpResponseMessage response;
                    string responseBody;

                    try
                    {
                        response = await client.PostAsJsonAsync("api/format", payload);
                        responseBody = await response.Content.ReadAsStringAsync();
                        success = response.IsSuccessStatusCode;
                        message = responseBody;

                        try
                        {
                            using var doc = System.Text.Json.JsonDocument.Parse(responseBody ?? "{}");
                            var root = doc.RootElement;
                            if (root.TryGetProperty("message", out var msg)) message = msg.GetString() ?? message;
                            if (root.TryGetProperty("success", out var succ)) success = succ.GetBoolean();
                        }
                        catch { /* keep raw */ }
                    }
                    catch (Exception httpEx)
                    {
                        _logger.LogError(httpEx, "HTTP request failed for BulkFormatMachine {IP}", item.MachineIP);
                        success = false;
                        message = $"Windows service unreachable: {httpEx.Message}";
                    }

                    // Audit log
                    if (machine != null)
                    {
                        _dbcontext.MachineFormatLogs.Add(new MachineFormatLog
                        {
                            MachineId = machine.Id,
                            MachineIP = item.MachineIP,
                            MachineName = machineName,
                            Status = success ? "Success" : "Failed",
                            ErrorMessage = success ? null : message,
                            RequestedByUserId = userId,
                            RequestedByUserName = userName,
                            RequestedAt = DateTime.Now,
                            ExecutedAt = DateTime.Now
                        });
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "BulkFormatMachine failed for {IP}", item.MachineIP);
                    success = false;
                    message = "Internal error.";
                }

                results.Add(new { machineIP = item.MachineIP, machineName, success, message });
            }

            await _dbcontext.SaveChangesAsync();

            return Json(new { results });
        }

        public class BulkFormatRequest
        {
            public List<FormatMachineApiRequest> Machines { get; set; } = new();
        }



        // ---------------------------------------------------------------
        //  FORMAT MACHINE  (Admin only)
        // ---------------------------------------------------------------
        [HttpPost]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> FormatMachine([FromBody] FormatMachineApiRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.MachineIP))
                return BadRequest(new { success = false, message = "MachineIP is required." });

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
            var userName = User.Identity?.Name ?? "";

            try
            {
                var client = _httpClientFactory.CreateClient("EmployeeApi");
                var payload = new
                {
                    MachineIP = request.MachineIP,
                    UserId = userId,
                    UserName = userName
                };

                HttpResponseMessage response;
                string responseBody = null;
                try
                {
                    response = await client.PostAsJsonAsync("api/format", payload);
                    responseBody = await response.Content.ReadAsStringAsync();
                }
                catch (Exception httpEx)
                {
                    _logger.LogError(httpEx, "HTTP request to Windows service failed for FormatMachine {IP}", request.MachineIP);

                    // Log failure in DB even when service is unreachable
                    var machine = await _dbcontext.AttendenceMachines
                        .FirstOrDefaultAsync(m => m.IpAddress == request.MachineIP);
                    if (machine != null)
                    {
                        _dbcontext.MachineFormatLogs.Add(new MachineFormatLog
                        {
                            MachineId = machine.Id,
                            MachineIP = request.MachineIP,
                            MachineName = machine.Name,
                            Status = "Failed",
                            ErrorMessage = $"Windows service unreachable: {httpEx.Message}",
                            RequestedByUserId = userId,
                            RequestedByUserName = userName,
                            RequestedAt = DateTime.Now,
                            ExecutedAt = DateTime.Now
                        });
                        await _dbcontext.SaveChangesAsync();
                    }

                    return StatusCode(502, new { success = false, message = "Failed to contact Windows service." });
                }

                bool success = response.IsSuccessStatusCode;
                string message = responseBody;

                try
                {
                    using var doc = System.Text.Json.JsonDocument.Parse(responseBody ?? "{}");
                    var root = doc.RootElement;
                    if (root.TryGetProperty("message", out var msg))
                        message = msg.GetString();
                    if (root.TryGetProperty("success", out var succ))
                        success = succ.GetBoolean();
                }
                catch { /* keep raw response */ }

                // Log in ASP.NET Core DB as well (audit trail)
                var machineDef = await _dbcontext.AttendenceMachines
                    .FirstOrDefaultAsync(m => m.IpAddress == request.MachineIP);
                if (machineDef != null)
                {
                    _dbcontext.MachineFormatLogs.Add(new MachineFormatLog
                    {
                        MachineId = machineDef.Id,
                        MachineIP = request.MachineIP,
                        MachineName = machineDef.Name,
                        Status = success ? "Success" : "Failed",
                        ErrorMessage = success ? null : message,
                        RequestedByUserId = userId,
                        RequestedByUserName = userName,
                        RequestedAt = DateTime.Now,
                        ExecutedAt = DateTime.Now
                    });
                    await _dbcontext.SaveChangesAsync();
                }

                return Json(new { success, message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "FormatMachine failed for {IP}", request.MachineIP);
                return StatusCode(500, new { success = false, message = "Internal error." });
            }
        }

        // ---------------------------------------------------------------
        //  FORMAT LOGS  (Admin only)
        // ---------------------------------------------------------------
        [HttpGet]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> GetFormatLogs(int machineId)
        {
            var logs = await _dbcontext.MachineFormatLogs
                .Where(l => l.MachineId == machineId)
                .OrderByDescending(l => l.RequestedAt)
                .Take(20)
                .Select(l => new
                {
                    l.Id,
                    l.Status,
                    l.ErrorMessage,
                    l.RequestedByUserName,
                    requestedAt = l.RequestedAt.ToString("yyyy-MM-dd HH:mm:ss")
                })
                .ToListAsync();

            return Json(logs);
        }

        public class FormatMachineApiRequest
        {
            public string MachineIP { get; set; }
        }
       
        [HttpGet]
        public async Task<IActionResult> ExportAttendanceMachines(string search = null, string sortColumn = null, string sortDirection = null)
        {
            var query = _dbcontext.AttendenceMachines.AsQueryable();

            // Apply search filter
            if (!string.IsNullOrEmpty(search))
            {
                var searchValue = search.ToLower();
                query = query.Where(m =>
                    m.Name.Contains(searchValue) ||
                    m.IpAddress.Contains(searchValue) ||
                    (m.Description ?? "").Contains(searchValue) ||
                    (m.Location ?? "").Contains(searchValue) ||
                    (search == "yes" && m.IsActive) ||
                    (search == "no" && !m.IsActive) ||
                    (search == "all" && m.IsFetchAll) ||
                    (search == "latest" && !m.IsFetchAll)
                );
            }

            // Apply sorting
            if (!string.IsNullOrEmpty(sortColumn))
            {
                switch (sortColumn)
                {
                    case "name":
                        query = sortDirection == "asc" ? query.OrderBy(m => m.Name) : query.OrderByDescending(m => m.Name);
                        break;
                    case "ipAddress":
                        query = sortDirection == "asc" ? query.OrderBy(m => m.IpAddress) : query.OrderByDescending(m => m.IpAddress);
                        break;
                    case "port":
                        query = sortDirection == "asc" ? query.OrderBy(m => m.Port) : query.OrderByDescending(m => m.Port);
                        break;
                    case "description":
                        query = sortDirection == "asc" ? query.OrderBy(m => m.Description) : query.OrderByDescending(m => m.Description);
                        break;
                    case "location":
                        query = sortDirection == "asc" ? query.OrderBy(m => m.Location) : query.OrderByDescending(m => m.Location);
                        break;
                    case "isActive":
                        query = sortDirection == "asc" ? query.OrderBy(m => m.IsActive) : query.OrderByDescending(m => m.IsActive);
                        break;
                    case "isFetchAll":
                        query = sortDirection == "asc" ? query.OrderBy(m => m.IsFetchAll) : query.OrderByDescending(m => m.IsFetchAll);
                        break;
                }
            }

            // Fetch all filtered records
            var records = await query.Select(m => new
            {
                Id = m.Id,
                Name = m.Name,
                IpAddress = m.IpAddress,
                Port = m.Port,
                Description = m.Description,
                Location = m.Location,
                IsActive = m.IsActive ? "Yes" : "No",
                IsFetchAll = m.IsFetchAll ? "All" : "Latest"
            }).ToListAsync();


            // Generate Excel file
            using (var workbook = new XLWorkbook())
            {
                IXLWorksheet worksheet = workbook.Worksheets.Add("Attendance Machines");

                // Headers
                worksheet.Cell(1, 1).Value = "ID";
                worksheet.Cell(1, 2).Value = "Name";
                worksheet.Cell(1, 3).Value = "IP Address";
                worksheet.Cell(1, 4).Value = "Port";
                worksheet.Cell(1, 5).Value = "Description";
                worksheet.Cell(1, 6).Value = "Location";
                worksheet.Cell(1, 7).Value = "Active";
                worksheet.Cell(1, 8).Value = "Fetch Records";

                int row = 2;
                foreach (var record in records)
                {
                    worksheet.Cell(row, 1).Value = record.Id;
                    worksheet.Cell(row, 2).Value = record.Name;
                    worksheet.Cell(row, 3).Value = record.IpAddress;
                    worksheet.Cell(row, 4).Value = record.Port;
                    worksheet.Cell(row, 5).Value = record.Description;
                    worksheet.Cell(row, 6).Value = record.Location;
                    worksheet.Cell(row, 7).Value = record.IsActive;
                    worksheet.Cell(row, 8).Value = record.IsFetchAll;

                    row++;
                }

                using (var stream = new MemoryStream())
                {
                    workbook.SaveAs(stream);
                    var content = stream.ToArray();
                    return File(content, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "AttendanceMachinesRecord.xlsx");
                }
            }
        }
    }
}
