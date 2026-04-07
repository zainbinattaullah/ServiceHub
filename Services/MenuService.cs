using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using ServiceHub.Areas.HR.Models;
using ServiceHub.Data;

namespace ServiceHub.Services
{
    public class MenuService : IMenuService
    {
        private readonly ServiceHubContext _db;
        private readonly IMemoryCache _cache;
        private const string CacheKeyAll = "menu_all_items";
        private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(30);

        public MenuService(ServiceHubContext db, IMemoryCache cache)
        {
            _db = db;
            _cache = cache;
        }

        // ── Public API ───────────────────────────────────────────────────

        public async Task<List<MenuNode>> GetMenuForRolesAsync(IEnumerable<string> roleIds)
        {
            var all = await GetAllItemsAsync();
            var roles = roleIds.ToHashSet(StringComparer.OrdinalIgnoreCase);

            // Collect restricted item IDs and their allowed roles
            // Items with zero RoleMenuItems rows are unrestricted (show to all).
            var restricted = all
                .Where(m => m.RoleMenuItems.Any())
                .ToDictionary(
                    m => m.Id,
                    m => m.RoleMenuItems.Select(r => r.RoleId).ToHashSet(StringComparer.OrdinalIgnoreCase));

            // Filter: item is visible when it is unrestricted OR current user's role is in allowed set
            bool IsVisible(MenuItem item) =>
                !restricted.ContainsKey(item.Id) ||
                restricted[item.Id].Overlaps(roles);

            // Build flat lookup of visible leaf items
            var visibleIds = all.Where(IsVisible).Select(m => m.Id).ToHashSet();

            // Build top-level nodes
            var topLevel = all
                .Where(m => m.ParentId == null && m.IsActive)
                .OrderBy(m => m.OrderIndex)
                .Select(parent =>
                {
                    // Children that are visible to this role
                    var visibleChildren = parent.Children
                        .Where(c => c.IsActive && visibleIds.Contains(c.Id))
                        .OrderBy(c => c.OrderIndex)
                        .Select(c => new MenuNode
                        {
                            Id = c.Id,
                            Name = c.Name,
                            Icon = c.Icon,
                            ResolvedUrl = c.ResolvedUrl,
                            IsGroup = false,
                            OrderIndex = c.OrderIndex
                        })
                        .ToList();

                    // Only include parent if it has visible children OR is itself visible
                    bool parentVisible = visibleIds.Contains(parent.Id);
                    if (!visibleChildren.Any() && !parentVisible) return null;

                    return new MenuNode
                    {
                        Id = parent.Id,
                        Name = parent.Name,
                        Icon = parent.Icon,
                        ResolvedUrl = parent.ResolvedUrl,
                        IsGroup = visibleChildren.Any(),
                        Children = visibleChildren,
                        OrderIndex = parent.OrderIndex
                    };
                })
                .Where(n => n != null)
                .Cast<MenuNode>()
                .ToList();

            return topLevel;
        }

        public async Task<List<MenuItem>> GetAllItemsAsync()
        {
            if (_cache.TryGetValue(CacheKeyAll, out List<MenuItem>? cached) && cached != null)
                return cached;

            var items = await _db.MenuItems
                .Include(m => m.Children.Where(c => c.IsActive))
                .Include(m => m.RoleMenuItems)
                .Where(m => m.IsActive)
                .AsNoTracking()
                .OrderBy(m => m.OrderIndex)
                .ToListAsync();

            _cache.Set(CacheKeyAll, items, CacheDuration);
            return items;
        }

        public void InvalidateCache() => _cache.Remove(CacheKeyAll);

        public async Task<bool> IsRouteAllowedAsync(string? area, string controller,
                                                     string action, IEnumerable<string> roleIds)
        {
            var all = await GetAllItemsAsync();
            var roles = roleIds.ToHashSet(StringComparer.OrdinalIgnoreCase);

            // Find the menu item matching this route
            var match = all.FirstOrDefault(m =>
                string.Equals(m.Controller, controller, StringComparison.OrdinalIgnoreCase) &&
                (string.IsNullOrEmpty(m.Action) ||
                 string.Equals(m.Action, action, StringComparison.OrdinalIgnoreCase)) &&
                string.Equals(m.Area ?? "", area ?? "", StringComparison.OrdinalIgnoreCase));

            if (match == null) return true;                   // not in menu → open access
            if (!match.RoleMenuItems.Any()) return true;      // no restrictions

            return match.RoleMenuItems.Any(r =>
                roles.Contains(r.RoleId, StringComparer.OrdinalIgnoreCase));
        }
    }
}
