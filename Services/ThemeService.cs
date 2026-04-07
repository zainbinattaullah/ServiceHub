using Microsoft.EntityFrameworkCore;
using ServiceHub.Areas.HR.Models;
using ServiceHub.Data;

namespace ServiceHub.Services
{
    public interface IThemeService
    {
        Task<UserSetting> GetSettingAsync(string userId);
        Task SaveSettingAsync(string userId, string theme, bool sidebarCollapsed);
    }

    public class ThemeService : IThemeService
    {
        private readonly ServiceHubContext _db;

        public ThemeService(ServiceHubContext db) => _db = db;

        public async Task<UserSetting> GetSettingAsync(string userId)
        {
            return await _db.UserSettings
                       .AsNoTracking()
                       .FirstOrDefaultAsync(u => u.UserId == userId)
                   ?? new UserSetting { UserId = userId, Theme = "light", SidebarCollapsed = false };
        }

        public async Task SaveSettingAsync(string userId, string theme, bool sidebarCollapsed)
        {
            // Validate theme value to prevent arbitrary data injection
            var allowed = new HashSet<string>
            {
                "light", "dark", "theme-blue", "theme-green",
                "theme-purple", "theme-orange"
            };
            if (!allowed.Contains(theme)) theme = "light";

            var setting = await _db.UserSettings
                              .FirstOrDefaultAsync(u => u.UserId == userId);

            if (setting == null)
            {
                _db.UserSettings.Add(new UserSetting
                {
                    UserId = userId,
                    Theme = theme,
                    SidebarCollapsed = sidebarCollapsed,
                    UpdatedAt = DateTime.UtcNow
                });
            }
            else
            {
                setting.Theme = theme;
                setting.SidebarCollapsed = sidebarCollapsed;
                setting.UpdatedAt = DateTime.UtcNow;
            }

            await _db.SaveChangesAsync();
        }
    }
}
