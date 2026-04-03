using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ServiceHub.Models;
using ServiceHub.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc.Rendering;
using System.Linq;
using System.Text.Json;

namespace ServiceHub.Areas.HR.Controllers
{
    [Area("HR")]
    [Authorize]
    public class EmployeeRegistrationController : Controller
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<EmployeeRegistrationController> _logger;
        private readonly ServiceHubContext _dbContext;
        private readonly IConfiguration _configuration;
        private readonly ServiceHub.Controllers.TimeWindowService _timeWindowService;

        public EmployeeRegistrationController(IHttpClientFactory httpClientFactory, ILogger<EmployeeRegistrationController> logger, ServiceHubContext dbContext, IConfiguration configuration, ServiceHub.Controllers.TimeWindowService timeWindowService)
        {
            _httpClient = httpClientFactory.CreateClient("EmployeeApi");
            _logger = logger;
            _dbContext = dbContext;
            _configuration = configuration;
            _timeWindowService = timeWindowService;
            if (_httpClient.BaseAddress == null)
            {
                var baseUrl = _configuration.GetValue<string>("EmployeeApiBaseUrl") ?? "http://localhost:5000/";
                _httpClient.BaseAddress = new Uri(baseUrl);
            }
        }

        private IQueryable<SelectListItem> GetMachineSelectListItems()
        {
            return _dbContext.AttendenceMachines
                .Where(m => m.IsActive)
                .Select(m => new SelectListItem { Value = m.IpAddress, Text = m.Location + " - " + m.IpAddress });
        }

        private IEnumerable<SelectListItem> GetPrivilegeOptions()
        {
            var configured = _configuration.GetSection("PrivilegeOptions").Get<string[]>();
            if (configured != null && configured.Length > 0)
            {
                return configured.Select(p => new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem { Value = p, Text = p });
            }
            return Enumerable.Empty<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem>();
        }

        [HttpGet]
        public async Task<IActionResult> Register()
        {
            var model = new EmployeeViewModel();
            model.MachineIPs = await GetMachineSelectListItems().ToListAsync();
            model.PrivilegeOptions = GetPrivilegeOptions();
            if (TempData.ContainsKey("SuccessMessage"))
            {
                ViewBag.Message = TempData["SuccessMessage"]?.ToString();
            }
            ViewBag.IsTransferWindowOpen = _timeWindowService.IsTransferWindowOpen();
            ViewBag.TransferWindowMessage = _timeWindowService.GetTransferWindowMessage();
            ViewBag.NextWindowChange = _timeWindowService.GetNextWindowChange()?.TotalMilliseconds;
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Register(EmployeeViewModel model)
        {
            if (!_timeWindowService.IsTransferWindowOpen())
            {
                model.MachineIPs = await GetMachineSelectListItems().ToListAsync();
                model.PrivilegeOptions = GetPrivilegeOptions();
                ViewBag.Alert = _timeWindowService.GetTransferWindowMessage();
                ViewBag.IsTransferWindowOpen = false;
                ViewBag.TransferWindowMessage = _timeWindowService.GetTransferWindowMessage();
                ViewBag.NextWindowChange = _timeWindowService.GetNextWindowChange()?.TotalMilliseconds;
                return View(model);
            }
            if (!ModelState.IsValid)
            {
                model.MachineIPs = await GetMachineSelectListItems().ToListAsync();
                model.PrivilegeOptions = GetPrivilegeOptions();
                return View(model);
            }

            try
            {
                var payload = new
                {
                    EmployeeCode = model.EmployeeCode,
                    EmployeeName = model.EmployeeName,
                    MachineIP = model.MachineIP,
                    Privilege = MapPrivilegeToInt(model.Privilege)
                };

                HttpResponseMessage response;
                string responseString = null;
                try
                {
                    response = await _httpClient.PostAsJsonAsync("api/employees/register/", payload);
                    responseString = await response.Content.ReadAsStringAsync();
                }
                catch (HttpRequestException httpEx)
                {
                    _logger.LogError(httpEx, "Failed to contact legacy enrollment service (BaseAddress={BaseAddress}).", _httpClient.BaseAddress);
                    var baseAddr = _httpClient.BaseAddress?.ToString() ?? "(no base address)";
                    ViewBag.Alert = $"Enrollment service is unavailable at {baseAddr}. Please ensure the legacy service is running and reachable.";
                    model.MachineIPs = await GetMachineSelectListItems().ToListAsync();
                    model.PrivilegeOptions = GetPrivilegeOptions();
                    return View(model);
                }

                bool success = false;
                string message = null;
                EmployeeEnrollResultDto? enrollResult = null;
                try
                {
                    enrollResult = JsonSerializer.Deserialize<EmployeeEnrollResultDto>(responseString ?? string.Empty, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    if (enrollResult != null)
                    {
                        success = enrollResult.Success;
                        message = enrollResult.Message;
                    }
                    else
                    {
                        using var doc = JsonDocument.Parse(responseString ?? string.Empty);
                        var root = doc.RootElement;
                        if (root.TryGetProperty("success", out var succ) && succ.ValueKind == JsonValueKind.True)
                            success = true;
                        if (root.TryGetProperty("message", out var msg) && msg.ValueKind == JsonValueKind.String)
                            message = msg.GetString();
                    }
                }
                catch (JsonException)
                {
                    try
                    {
                        using var doc = JsonDocument.Parse(responseString ?? string.Empty);
                        var root = doc.RootElement;
                        if (root.TryGetProperty("success", out var succ) && succ.ValueKind == JsonValueKind.True)
                            success = true;
                        if (root.TryGetProperty("message", out var msg) && msg.ValueKind == JsonValueKind.String)
                            message = msg.GetString();
                    }
                    catch { }
                }

                // Insert enrollment record regardless of API success
                var enrollment = new ServiceHub.Areas.HR.Models.EmployeeEnrollment
                {
                    EmployeeCode = model.EmployeeCode,
                    EmployeeName = model.EmployeeName,
                    MachineIP = model.MachineIP,
                    Privilege = model.Privilege,
                    CreatedBy = User?.Identity?.Name,
                    CreatedAt = DateTime.UtcNow,
                    IsSynced = success,
                    SyncMessage = message,
                    SyncedAt = success ? DateTime.UtcNow : (DateTime?)null
                };
                var machine = await _dbContext.AttendenceMachines.FirstOrDefaultAsync(m => m.IpAddress == model.MachineIP);
                if (machine != null) enrollment.MachineId = machine.Id;
                _dbContext.EmployeeEnrollments.Add(enrollment);
                await _dbContext.SaveChangesAsync();
                if (success)
                {
                    TempData["SuccessMessage"] = "Employee registered and synced successfully.";
                    return RedirectToAction(nameof(Register));
                }
                string alertMsg;
                if (response == null)
                {
                    alertMsg = "No response from enrollment service. Enrollment saved locally for retry.";
                }
                else if (response.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable || response.StatusCode == System.Net.HttpStatusCode.GatewayTimeout)
                {
                    alertMsg = "Device appears to be offline or not responding. Enrollment saved locally for retry.";
                }
                else if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    alertMsg = "Enrollment endpoint not found on enrollment service. Please verify service configuration.";
                }
                else
                {
                    alertMsg = message ?? "Failed to register employee on device. Enrollment saved locally for retry.";
                }

                ViewBag.Alert = alertMsg;
                // repopulate lists and return view
                model.MachineIPs = await GetMachineSelectListItems().ToListAsync();
                model.PrivilegeOptions = GetPrivilegeOptions();
                return View(model);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while registering employee");
                ModelState.AddModelError(string.Empty, "An error occurred while registering the employee.");
                model.MachineIPs = await GetMachineSelectListItems().ToListAsync();
                model.PrivilegeOptions = GetPrivilegeOptions();
                return View(model);
            }
        }

        private int MapPrivilegeToInt(string? privilege)
        {
            if (string.IsNullOrWhiteSpace(privilege))
                return 0; // default

            // If the frontend already passes numeric values as strings, accept them
            if (int.TryParse(privilege, out var numeric)) 
                return numeric;

            switch (privilege.Trim().ToLowerInvariant())
            {
                case "user":
                    return 0;
                case "superadmin":
                case "admin":
                    return 1;
                    default:
                        _logger.LogWarning("Unknown privilege mapping for '{Privilege}', defaulting to 0.", privilege);
                        return 0;
                }

        }

        // DTO that matches the legacy service response
        private class EmployeeEnrollResultDto
        {
            public bool Success { get; set; }
            public string? Message { get; set; }
            public bool IsDeviceOffline { get; set; }
        }
    }
}