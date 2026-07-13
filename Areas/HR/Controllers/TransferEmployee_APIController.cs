using DocumentFormat.OpenXml.InkML;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ServiceHub.Controllers;
using ServiceHub.Data;
using ServiceHub.Models;

namespace ServiceHub.Areas.HR.Controllers
{

    [Area("HR")]
    [Authorize]
    public class TransferEmployee_APIController : Controller
    {
        private readonly ServiceHubContext _dbcontext;
        private readonly TimeWindowService _timeWindowService;
        private readonly ILogger<TransferEmployee_APIController> _logger;

        public TransferEmployee_APIController(ServiceHubContext dbcontext,TimeWindowService timeWindowService,ILogger<TransferEmployee_APIController> logger)
        {
            _dbcontext = dbcontext;
            _timeWindowService = timeWindowService;
            _logger = logger;
        }
        public IActionResult Index()
        {
            ViewBag.IsTransferWindowOpen = _timeWindowService.IsTransferWindowOpen();
            ViewBag.TransferWindowMessage = _timeWindowService.GetTransferWindowMessage();
            ViewBag.NextWindowChange = _timeWindowService.GetNextWindowChange()?.TotalMilliseconds;
            return View();
        }        
        public async Task<IActionResult> GetMachineIPs()
        {
            try
            {
                // Exclude ADMS push machines (SerialNumber != null) — they cannot be
                // accessed via ZKemKeeper DLL, so transfer source/destination is meaningless.
                var machines = await _dbcontext.AttendenceMachines
                    .Where(m => m.IsActive && (m.SerialNumber == null || m.SerialNumber == ""))
                    .Select(m => new { m.IpAddress, m.Port, m.Location, m.Name })
                    .ToListAsync();

                var result = machines.Select(m =>
                {
                    string displayName = (!string.IsNullOrWhiteSpace(m.Location) ? m.Location : m.Name) + " - " + m.IpAddress;
                    return new
                    {
                        Value = m.IpAddress,
                        Label = displayName
                    };
                });

                return Json(result);
            }
            catch (Exception ex)
            {
                return StatusCode(500, "Error getting machine IPs: " + ex.Message);
            }
        }
        // Endpoint to transfer employees
        [HttpPost]
        public async Task<IActionResult> TransferMultipleEmployees([FromBody] MultipleTransferRequest transferRequest)
        {
            try
            {
                if (!_timeWindowService.IsTransferWindowOpen())
                {
                    _logger.LogWarning("Transfer attempted outside allowed window");
                    return StatusCode(403, new
                    {
                        SuccessCount = 0,
                        FailCount = 0,
                        Message = _timeWindowService.GetTransferWindowMessage()
                    });
                }
                var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
                if (string.IsNullOrEmpty(userId))
                {
                    return Unauthorized("User is not authenticated.");
                }
                // Call the service
                using (var client = new HttpClient())
                {
                    client.Timeout = TimeSpan.FromMinutes(5);
                    var response = await client.PostAsJsonAsync("http://localhost:5000/api/transfer/", new
                    {
                        transferRequest.SourceIP,
                        transferRequest.DestinationIPs,
                        transferRequest.Employees,
                        transferRequest.TransferAllEmployees,
                        transferRequest.TransferAllMachines,
                        UserId = userId
                    });
                    var responseContent = await response.Content.ReadAsStringAsync();
                    if (response.IsSuccessStatusCode)
                    {
                        return Content(responseContent, "application/json");
                    }
                    return StatusCode((int)response.StatusCode, responseContent);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during employee transfer");
                return StatusCode(500, new
                {
                    SuccessCount = 0,
                    FailCount = 0,
                    Message = $"Internal server error: {ex.Message}"
                });
            }
        }
        [HttpGet]
        public IActionResult CheckTransferWindow()
        {
            return Json(new
            {
                isOpen = _timeWindowService.IsTransferWindowOpen(),
                message = _timeWindowService.GetTransferWindowMessage(),
                nextCheckInMs = _timeWindowService.GetNextWindowChange()?.TotalMilliseconds ?? 30000
            });
        }
        [HttpGet]
        public IActionResult IsTransferWindowOpen()
        {
            return Json(_timeWindowService.IsTransferWindowOpen());
        }
        [HttpGet]
        public IActionResult GetTransferWindows()
        {
            try
            {
                var windows = _timeWindowService.GetAllWindows()
                    .Where(w => w.Kind == TimeWindowService.WindowKind.Transfer)
                    .Select(w => new
                    {
                        Start     = DateTime.Today.Add(w.Start).ToString("hh:mm tt"),
                        End       = DateTime.Today.Add(w.End  ).ToString("hh:mm tt"),
                        IsCurrent = w.IsCurrent
                    })
                    .ToList();

                return Json(windows);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting transfer windows");
                return StatusCode(500, "Error getting transfer schedule");
            }
        }        

        // Endpoint to fetch employees for specific IPs
        [HttpPost]
        public async Task<IActionResult> GetEmployees([FromBody] List<string> machineIPs)
        {
            try
            {
                if (!_timeWindowService.IsTransferWindowOpen())
            {
                return StatusCode(403, new
                {
                    SuccessCount = 0,
                    FailCount = 0,
                    Message = _timeWindowService.GetTransferWindowMessage()
                });
            }
            using (var client = new HttpClient())
            {
                // Increase timeout to 5 minutes
                client.Timeout = TimeSpan.FromMinutes(5);
                try
                {
                    //Console.WriteLine($"Fetching employees for IPs: {string.Join(", ", machineIPs)}");
                    // Call the external API
                    var response = await client.PostAsJsonAsync("http://localhost:5000/api/employees/", machineIPs);
                    var responseContent = await response.Content.ReadAsStringAsync();
                    //Console.WriteLine($"Response from Windows Service: {responseContent}");

                    if (response.IsSuccessStatusCode)
                    {
                        // Parse response using JObject
                        var jsonObject = JObject.Parse(responseContent);
                        // Ensure "employees" exists and is an array
                        if (jsonObject["employees"] is JArray employeesArray)
                        {
                            var employees = new List<object>();
                            // Loop through the "employees" array
                            foreach (var item in employeesArray)
                            {
                                // Check if item is another array (nested structure)
                                if (item is JArray nestedArray)
                                {
                                    employees.AddRange(nestedArray.Select(emp => new
                                    {
                                        EmpNo = (string)emp["EmpNo"],
                                        EmpName = (string?)emp["EmpName"]
                                    }));
                                }
                                else
                                {
                                    // Handle flat structure
                                    employees.Add(new
                                    {
                                        EmpNo = (string)item["EmpNo"],
                                        EmpName = (string?)item["EmpName"]
                                    });
                                }
                            }
                            return Ok(employees);
                        }
                        return BadRequest("Invalid response format: 'employees' node missing or incorrect.");
                    }
                    else
                    {
                        //Console.WriteLine($"Error from Windows Service: {responseContent}");
                        return StatusCode((int)response.StatusCode, $"Failed to fetch employees. Error: {responseContent}");
                    }
                }
                catch (Exception ex)
                {
                    //Console.WriteLine($"Exception: {ex.Message}");
                    return StatusCode(500, $"Internal server error: {ex.Message}");
                }
            }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching employees");
                return StatusCode(500, $"Internal server error: {ex.Message}");
            }
        }       

        // Endpoint to delete employees from source machine
        [HttpPost]
        public async Task<IActionResult> DeleteEmployees([FromBody] DeleteEmployeesRequest request)
        {
            try
            {
                if (!_timeWindowService.IsTransferWindowOpen())
                {
                    return StatusCode(403, new
                    {
                        SuccessCount = 0,
                        FailCount = 0,
                        Message = _timeWindowService.GetTransferWindowMessage()
                    });
                }

                if (request == null || string.IsNullOrWhiteSpace(request.SourceIP) || request.Employees == null || request.Employees.Count == 0)
                {
                    return BadRequest(new { SuccessCount = 0, FailCount = 0, Message = "SourceIP and at least one employee are required." });
                }

                int successCount = 0;
                int failCount = 0;
                var errors = new List<string>();

                foreach (var emp in request.Employees)
                {
                    try
                    {
                        using (var client = new HttpClient())
                        {
                            client.Timeout = TimeSpan.FromMinutes(2);
                            var response = await client.PostAsJsonAsync("http://localhost:5000/api/employees/delete/", new
                            {
                                EmployeeCode = emp.EmpNo,
                                MachineIP = request.SourceIP
                            });

                            if (!response.IsSuccessStatusCode)
                            {
                                string body = await response.Content.ReadAsStringAsync();
                                failCount++;
                                errors.Add($"{emp.EmpNo}: {body}");
                                _logger.LogWarning("Delete API returned {Status} for {EmpNo} on {IP}: {Body}",
                                    (int)response.StatusCode, emp.EmpNo, request.SourceIP, body);
                                continue;
                            }

                            // Parse response to check device result
                            string respBody = await response.Content.ReadAsStringAsync();
                            bool apiSuccess = false;
                            try
                            {
                                using var doc = System.Text.Json.JsonDocument.Parse(respBody);
                                var root = doc.RootElement;
                                if (root.TryGetProperty("success", out var s))
                                    apiSuccess = s.GetBoolean();
                            }
                            catch { }

                            if (!apiSuccess)
                            {
                                failCount++;
                                errors.Add($"{emp.EmpNo}: device returned failure");
                                continue;
                            }
                        }

                        // Clean up any DB records for this employee on this machine
                        try
                        {
                            var enrollments = await _dbcontext.EmployeeEnrollments
                                .Where(e => e.EmployeeCode == emp.EmpNo && e.MachineIP == request.SourceIP)
                                .ToListAsync();
                            if (enrollments.Any())
                                _dbcontext.EmployeeEnrollments.RemoveRange(enrollments);

                            var bioLogs = await _dbcontext.Employee_Biometric_Log
                                .Where(b => b.EmpNo == emp.EmpNo && b.MachineIP == request.SourceIP)
                                .ToListAsync();
                            if (bioLogs.Any())
                                _dbcontext.Employee_Biometric_Log.RemoveRange(bioLogs);

                            await _dbcontext.SaveChangesAsync();
                        }
                        catch (Exception dbEx)
                        {
                            _logger.LogWarning(dbEx, "DB cleanup failed for {EmpNo} on {IP}", emp.EmpNo, request.SourceIP);
                        }

                        successCount++;
                        _logger.LogInformation("Deleted employee {EmpNo} from device {IP}", emp.EmpNo, request.SourceIP);
                    }
                    catch (Exception ex)
                    {
                        failCount++;
                        errors.Add($"{emp.EmpNo}: {ex.Message}");
                        _logger.LogError(ex, "Delete failed for {EmpNo} on {IP}", emp.EmpNo, request.SourceIP);
                    }
                }

                string summary = $"[SUMMARY] Success: {successCount}, Failed: {failCount}.";
                if (errors.Any())
                    summary += " Errors: " + string.Join("; ", errors);

                return Ok(new { SuccessCount = successCount, FailCount = failCount, Message = summary });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during employee deletion");
                return StatusCode(500, new
                {
                    SuccessCount = 0,
                    FailCount = 0,
                    Message = $"Internal server error: {ex.Message}"
                });
            }
        }

        // Model for multiple transfer request
        public class MultipleTransferRequest
        {
            public string SourceIP { get; set; }
            public List<string> DestinationIPs { get; set; }
            public List<EmployeeTransfer> Employees { get; set; }
            public bool TransferAllEmployees { get; set; }
            public bool TransferAllMachines { get; set; }
        }

        public class EmployeeTransfer
        {
            public string EmpNo { get; set; }
            public string? EmpName { get; set; }
        }

        public class DeleteEmployeesRequest
        {
            public string SourceIP { get; set; }
            public List<EmployeeTransfer> Employees { get; set; }
        }
    }
}