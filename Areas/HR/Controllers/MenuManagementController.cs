using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ServiceHub.Areas.HR.Models;
using ServiceHub.Data;
using ServiceHub.Services;

namespace ServiceHub.Areas.HR.Controllers
{
    [Area("HR")]
    [Authorize(Roles = "Admin")]
    public class MenuManagementController : Controller
    {
        private readonly ServiceHubContext _db;
        private readonly RoleManager<IdentityRole> _roleManager;
        private readonly IMenuService _menuService;

        public MenuManagementController(ServiceHubContext db,
                                        RoleManager<IdentityRole> roleManager,
                                        IMenuService menuService)
        {
            _db          = db;
            _roleManager = roleManager;
            _menuService = menuService;
        }

        // ── GET: Index — list all menu items ────────────────────────────
        public async Task<IActionResult> Index()
        {
            var items = await _db.MenuItems
                .Include(m => m.Parent)
                .OrderBy(m => m.ParentId)
                .ThenBy(m => m.OrderIndex)
                .AsNoTracking()
                .ToListAsync();

            return View(items);
        }

        // ── GET: Create ──────────────────────────────────────────────────
        public async Task<IActionResult> Create()
        {
            ViewBag.Parents = await _db.MenuItems
                .Where(m => m.ParentId == null && m.IsActive)
                .OrderBy(m => m.OrderIndex)
                .AsNoTracking()
                .ToListAsync();
            return View(new MenuItem());
        }

        // ── POST: Create ─────────────────────────────────────────────────
        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(MenuItem model)
        {
            if (!ModelState.IsValid)
            {
                ViewBag.Parents = await GetParentListAsync();
                return View(model);
            }

            _db.MenuItems.Add(model);
            await _db.SaveChangesAsync();
            _menuService.InvalidateCache();

            TempData["Success"] = $"Menu item \"{model.Name}\" created.";
            return RedirectToAction(nameof(Index));
        }

        // ── GET: Edit ────────────────────────────────────────────────────
        public async Task<IActionResult> Edit(int id)
        {
            var item = await _db.MenuItems.FindAsync(id);
            if (item == null) return NotFound();

            ViewBag.Parents = await GetParentListAsync(id);
            return View(item);
        }

        // ── POST: Edit ───────────────────────────────────────────────────
        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, MenuItem model)
        {
            if (id != model.Id) return BadRequest();

            if (!ModelState.IsValid)
            {
                ViewBag.Parents = await GetParentListAsync(id);
                return View(model);
            }

            _db.MenuItems.Update(model);
            await _db.SaveChangesAsync();
            _menuService.InvalidateCache();

            TempData["Success"] = $"Menu item \"{model.Name}\" updated.";
            return RedirectToAction(nameof(Index));
        }

        // ── POST: Toggle Active ──────────────────────────────────────────
        [HttpPost]
        public async Task<IActionResult> ToggleActive(int id)
        {
            var item = await _db.MenuItems.FindAsync(id);
            if (item == null) return NotFound();

            item.IsActive = !item.IsActive;
            await _db.SaveChangesAsync();
            _menuService.InvalidateCache();

            return Json(new { success = true, isActive = item.IsActive });
        }

        // ── POST: Delete ─────────────────────────────────────────────────
        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            var item = await _db.MenuItems
                .Include(m => m.Children)
                .Include(m => m.RoleMenuItems)
                .FirstOrDefaultAsync(m => m.Id == id);

            if (item == null) return NotFound();

            if (item.Children.Any())
            {
                TempData["Error"] = "Cannot delete a parent item that has children. Delete or re-parent the children first.";
                return RedirectToAction(nameof(Index));
            }

            _db.RoleMenuItems.RemoveRange(item.RoleMenuItems);
            _db.MenuItems.Remove(item);
            await _db.SaveChangesAsync();
            _menuService.InvalidateCache();

            TempData["Success"] = $"Menu item \"{item.Name}\" deleted.";
            return RedirectToAction(nameof(Index));
        }

        // ── GET: RoleAssignment — manage which roles see a menu item ─────
        public async Task<IActionResult> RoleAssignment(int id)
        {
            var item = await _db.MenuItems
                .Include(m => m.RoleMenuItems)
                .FirstOrDefaultAsync(m => m.Id == id);
            if (item == null) return NotFound();

            var allRoles  = await _roleManager.Roles.AsNoTracking().ToListAsync();
            var assigned  = item.RoleMenuItems.Select(r => r.RoleId).ToHashSet();

            ViewBag.MenuItem = item;
            ViewBag.AllRoles = allRoles;
            ViewBag.Assigned = assigned;

            return View();
        }

        // ── POST: RoleAssignment ─────────────────────────────────────────
        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> RoleAssignment(int id, List<string> selectedRoleIds)
        {
            var item = await _db.MenuItems
                .Include(m => m.RoleMenuItems)
                .FirstOrDefaultAsync(m => m.Id == id);
            if (item == null) return NotFound();

            // Remove all existing role assignments for this item
            _db.RoleMenuItems.RemoveRange(item.RoleMenuItems);

            // Add the newly selected ones
            foreach (var roleId in selectedRoleIds ?? new List<string>())
            {
                _db.RoleMenuItems.Add(new RoleMenuItem
                {
                    MenuItemId = id,
                    RoleId     = roleId
                });
            }

            await _db.SaveChangesAsync();
            _menuService.InvalidateCache();

            TempData["Success"] = selectedRoleIds?.Any() == true
                ? $"Role access updated: {selectedRoleIds.Count} role(s) assigned."
                : "All role restrictions removed — item is now visible to all users.";

            return RedirectToAction(nameof(Index));
        }

        // ── GET: RoleManagement — assign users to roles ──────────────────
        public async Task<IActionResult> RoleManagement()
        {
            var roles = await _roleManager.Roles.AsNoTracking().ToListAsync();
            return View(roles);
        }

        // ── GET: RoleMenuAssignment — role-centric menu assignment screen ─
        public async Task<IActionResult> RoleMenuAssignment()
        {
            var roles = await _roleManager.Roles
                .OrderBy(r => r.Name)
                .AsNoTracking()
                .ToListAsync();
            return View(roles);
        }

        // ── GET: GetRoleMenuItems — AJAX: all menu items + assigned flag ──
        [HttpGet]
        public async Task<IActionResult> GetRoleMenuItems(string roleId)
        {
            if (string.IsNullOrEmpty(roleId))
                return Json(new List<object>());

            var allItems = await _db.MenuItems
                .Include(m => m.RoleMenuItems)
                .OrderBy(m => m.ParentId == null ? 0 : 1)
                .ThenBy(m => m.OrderIndex)
                .AsNoTracking()
                .ToListAsync();

            var result = allItems.Select(m => new
            {
                m.Id,
                m.Name,
                m.Icon,
                m.ParentId,
                m.OrderIndex,
                m.IsActive,
                isGroup      = m.ParentId == null,
                isAssigned   = m.RoleMenuItems.Any(rm => rm.RoleId == roleId),
                isRestricted = m.RoleMenuItems.Any()   // false = visible to everyone
            }).ToList();

            return Json(result);
        }

        // ── POST: SaveRoleMenuAssignment — AJAX: replace role's assignments─
        [HttpPost]
        public async Task<IActionResult> SaveRoleMenuAssignment([FromBody] RoleMenuSaveRequest request)
        {
            if (request == null || string.IsNullOrEmpty(request.RoleId))
                return Json(new { success = false, message = "Invalid request." });

            var role = await _roleManager.FindByIdAsync(request.RoleId);
            if (role == null)
                return Json(new { success = false, message = "Role not found." });

            var existing = await _db.RoleMenuItems
                .Where(rm => rm.RoleId == request.RoleId)
                .ToListAsync();

            _db.RoleMenuItems.RemoveRange(existing);

            foreach (var menuId in request.MenuItemIds ?? new List<int>())
            {
                _db.RoleMenuItems.Add(new RoleMenuItem
                {
                    RoleId     = request.RoleId,
                    MenuItemId = menuId
                });
            }

            await _db.SaveChangesAsync();
            _menuService.InvalidateCache();

            return Json(new
            {
                success = true,
                message = request.MenuItemIds?.Count > 0
                    ? $"Saved {request.MenuItemIds.Count} menu item(s) for role \"{role.Name}\"."
                    : $"All restrictions removed — \"{role.Name}\" sees every unrestricted menu item."
            });
        }

        public class RoleMenuSaveRequest
        {
            public string       RoleId      { get; set; }
            public List<int>    MenuItemIds { get; set; }
        }

        // ── GET: ReOrder — update order via drag-and-drop ────────────────
        [HttpPost]
        public async Task<IActionResult> UpdateOrder([FromBody] List<OrderUpdateItem> items)
        {
            if (items == null) return BadRequest();

            foreach (var item in items)
            {
                var mi = await _db.MenuItems.FindAsync(item.Id);
                if (mi != null)
                {
                    mi.OrderIndex = item.Order;
                    mi.ParentId   = item.ParentId == 0 ? null : item.ParentId;
                }
            }

            await _db.SaveChangesAsync();
            _menuService.InvalidateCache();

            return Json(new { success = true });
        }

        // ── Helpers ──────────────────────────────────────────────────────
        private async Task<List<MenuItem>> GetParentListAsync(int excludeId = 0)
        {
            return await _db.MenuItems
                .Where(m => m.ParentId == null && m.Id != excludeId)
                .OrderBy(m => m.OrderIndex)
                .AsNoTracking()
                .ToListAsync();
        }

        public class OrderUpdateItem
        {
            public int Id       { get; set; }
            public int Order    { get; set; }
            public int ParentId { get; set; }
        }
    }
}
