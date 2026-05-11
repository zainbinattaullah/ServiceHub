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
                var machines = await _dbcontext.AttendenceMachines
                    .Where(m => m.IsActive)
                    .Select(m => new { m.IpAddress, m.Port, m.Location, m.Name })
                    .ToListAsync();

                var statusTasks = machines.Select(async m =>
                {
                    bool isOnline = await Task.Run(() => IsMachineReachable(m.IpAddress, m.Port));
                    string displayName = (!string.IsNullOrWhiteSpace(m.Location) ? m.Location : m.Name) + " - " + m.IpAddress;
                    return new
                    {
                        Value    = m.IpAddress,
                        Label    = displayName + (isOnline ? " (Online)" : " (Offline)"),
                        IsOnline = isOnline
                    };
                });

                var result = await Task.WhenAll(statusTasks);
                return Json(result.OrderByDescending(m => m.IsOnline).ThenBy(m => m.Label));
            }
            catch (Exception ex)
            {
                return StatusCode(500, "Error getting machine IPs: " + ex.Message);
            }
        }
        private static bool IsMachineReachable(string ip, int port = 4370)
        {
            // Layer 1: ICMP ping
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
    }
}