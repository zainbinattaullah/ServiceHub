using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ServiceHub.Models;

namespace ServiceHub.Controllers
{
    [Authorize(Roles = "Admin")]
    public class UsersController : Controller
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly RoleManager<IdentityRole> _roleManager;

        public UsersController(UserManager<ApplicationUser> userManager, RoleManager<IdentityRole> roleManager)
        {
            _userManager = userManager;
            _roleManager = roleManager;
        }

        public IActionResult Index()
        {
            return View();
        }
        [HttpPost]
        public async Task<IActionResult> GetUsers()
        {
            var request = HttpContext.Request.Form;
            var draw = request["draw"].FirstOrDefault();
            var start = Convert.ToInt32(request["start"].FirstOrDefault() ?? "0");
            var length = Convert.ToInt32(request["length"].FirstOrDefault() ?? "10");
            var searchValue = request["search[value]"].FirstOrDefault();
            var sortColumnIndex = request["order[0][column]"].FirstOrDefault();
            var sortDirection = request["order[0][dir]"].FirstOrDefault();

            var users = _userManager.Users.AsNoTracking();

            if (!string.IsNullOrEmpty(searchValue))
            {
                var sv = searchValue.ToLower();
                users = users.Where(u =>
                    (u.FirstName + " " + u.LastName).ToLower().Contains(sv) ||
                    u.Email.ToLower().Contains(sv) ||
                    (u.UserName ?? "").ToLower().Contains(sv)
                );
            }

            users = (sortColumnIndex, sortDirection) switch
            {
                ("0", "asc") => users.OrderBy(u => u.FirstName),
                ("0", _) => users.OrderByDescending(u => u.FirstName),
                ("1", "asc") => users.OrderBy(u => u.Email),
                ("1", _) => users.OrderByDescending(u => u.Email),
                _ => users.OrderBy(u => u.FirstName)
            };

            var totalRecords = await users.CountAsync();
            var pagedUsers = await users.Skip(start).Take(length).ToListAsync();

            var data = new List<object>();
            foreach (var user in pagedUsers)
            {
                var roles = await _userManager.GetRolesAsync(user);
                data.Add(new
                {
                    id = user.Id,
                    fullName = $"{user.FirstName} {user.LastName}".Trim(),
                    email = user.Email,
                    roles = roles.ToList(),
                    isLockedOut = user.LockoutEnd.HasValue && user.LockoutEnd > DateTimeOffset.UtcNow
                });
            }

            return Json(new { draw, recordsTotal = totalRecords, recordsFiltered = totalRecords, data });
        }

        [HttpGet]
        public async Task<IActionResult> Edit(string id)
        {
            if (string.IsNullOrEmpty(id)) return NotFound();

            var user = await _userManager.FindByIdAsync(id);
            if (user == null) return NotFound();

            var allRoles = await _roleManager.Roles.Select(r => r.Name).ToListAsync();
            var userRoles = await _userManager.GetRolesAsync(user);

            var vm = new EditUserViewModel
            {
                Id = user.Id,
                FirstName = user.FirstName,
                LastName = user.LastName,
                Email = user.Email,
                AllRoles = allRoles,
                SelectedRoles = userRoles.ToList()
            };

            return View(vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(EditUserViewModel model)
        {
            if (!ModelState.IsValid)
            {
                model.AllRoles = await _roleManager.Roles.Select(r => r.Name).ToListAsync();
                return View(model);
            }

            var user = await _userManager.FindByIdAsync(model.Id);
            if (user == null) return NotFound();

            user.FirstName = model.FirstName;
            user.LastName = model.LastName;

            var updateResult = await _userManager.UpdateAsync(user);
            if (!updateResult.Succeeded)
            {
                foreach (var e in updateResult.Errors)
                    ModelState.AddModelError("", e.Description);
                model.AllRoles = await _roleManager.Roles.Select(r => r.Name).ToListAsync();
                return View(model);
            }

            // Sync roles
            var currentRoles = await _userManager.GetRolesAsync(user);
            var selectedRoles = model.SelectedRoles ?? new List<string>();

            var toRemove = currentRoles.Except(selectedRoles).ToList();
            var toAdd = selectedRoles.Except(currentRoles).ToList();

            if (toRemove.Any())
                await _userManager.RemoveFromRolesAsync(user, toRemove);
            if (toAdd.Any())
                await _userManager.AddToRolesAsync(user, toAdd);

            TempData["Success"] = $"User '{user.Email}' updated successfully.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        public async Task<IActionResult> ToggleLock(string id)
        {
            var user = await _userManager.FindByIdAsync(id);
            if (user == null) return NotFound();

            bool isLocked = user.LockoutEnd.HasValue && user.LockoutEnd > DateTimeOffset.UtcNow;
            if (isLocked)
                await _userManager.SetLockoutEndDateAsync(user, null);
            else
                await _userManager.SetLockoutEndDateAsync(user, DateTimeOffset.UtcNow.AddYears(100));

            return Json(new { success = true, locked = !isLocked });
        }

        [HttpPost]
        public async Task<IActionResult> ResetPassword(string id, string newPassword)
        {
            if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 6)
                return Json(new { success = false, message = "Password must be at least 6 characters." });

            var user = await _userManager.FindByIdAsync(id);
            if (user == null) return NotFound();

            var token = await _userManager.GeneratePasswordResetTokenAsync(user);
            var result = await _userManager.ResetPasswordAsync(user, token, newPassword);

            if (result.Succeeded)
                return Json(new { success = true, message = "Password reset successfully." });

            var errors = string.Join(", ", result.Errors.Select(e => e.Description));
            return Json(new { success = false, message = errors });
        }
    }
}
