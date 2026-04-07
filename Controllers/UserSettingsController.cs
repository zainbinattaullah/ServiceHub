using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using ServiceHub.Services;

namespace ServiceHub.Controllers
{
    [Authorize]
    public class UserSettingsController : Controller
    {
        private readonly IThemeService _themeService;
        private readonly UserManager<ApplicationUser> _userManager;

        public UserSettingsController(IThemeService themeService,
                                      UserManager<ApplicationUser> userManager)
        {
            _themeService  = themeService;
            _userManager   = userManager;
        }

        // ── GET /UserSettings/GetSetting ──────────────────────────────────
        [HttpGet]
        public async Task<IActionResult> GetSetting()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return Json(new { theme = "light", sidebarCollapsed = false });

            var setting = await _themeService.GetSettingAsync(user.Id);
            return Json(new
            {
                theme            = setting.Theme,
                sidebarCollapsed = setting.SidebarCollapsed
            });
        }

        // ── POST /UserSettings/SaveSetting ────────────────────────────────
        [HttpPost]
        public async Task<IActionResult> SaveSetting([FromBody] SaveSettingRequest req)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return Unauthorized();

            await _themeService.SaveSettingAsync(user.Id,
                req.Theme ?? "light",
                req.SidebarCollapsed);

            return Json(new { success = true });
        }

        public class SaveSettingRequest
        {
            public string? Theme { get; set; }
            public bool SidebarCollapsed { get; set; }
        }
    }
}
