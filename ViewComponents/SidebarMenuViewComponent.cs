using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using ServiceHub.Services;

namespace ServiceHub.ViewComponents
{
    /// <summary>
    /// Builds the role-filtered navigation tree and passes it to the sidebar partial.
    /// Invoked from _Sidebar.cshtml via:  @await Component.InvokeAsync("SidebarMenu")
    /// </summary>
    public class SidebarMenuViewComponent : ViewComponent
    {
        private readonly IMenuService _menuService;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly RoleManager<IdentityRole> _roleManager;

        public SidebarMenuViewComponent(
            IMenuService menuService,
            UserManager<ApplicationUser> userManager,
            RoleManager<IdentityRole> roleManager)
        {
            _menuService = menuService;
            _userManager = userManager;
            _roleManager = roleManager;
        }

        public async Task<IViewComponentResult> InvokeAsync()
        {
            // If not authenticated return an empty menu
            if (UserClaimsPrincipal?.Identity?.IsAuthenticated != true)
                return View(new List<MenuNode>());

            var user = await _userManager.GetUserAsync(UserClaimsPrincipal);
            if (user == null) return View(new List<MenuNode>());

            // Resolve role names → role IDs for the RoleMenuItems lookup
            var roleNames = await _userManager.GetRolesAsync(user);
            var roleIds = new List<string>();
            foreach (var rn in roleNames)
            {
                var role = await _roleManager.FindByNameAsync(rn);
                if (role != null) roleIds.Add(role.Id);
            }

            var nodes = await _menuService.GetMenuForRolesAsync(roleIds);
            return View(nodes);
        }
    }
}
