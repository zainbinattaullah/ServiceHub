using Microsoft.AspNetCore.Identity;
using ServiceHub.Services;

namespace ServiceHub.Middleware
{
    /// <summary>
    /// URL-level RBAC guard based on the MenuItems / RoleMenuItems tables.
    ///
    /// Rules (in order):
    ///  1. Static files, Identity pages, API paths → always pass through.
    ///  2. Unauthenticated requests → pass through (login redirect handled elsewhere).
    ///  3. Admin role → always pass through (super-user bypass).
    ///  4. Route not found in MenuItems → pass through (open access).
    ///  5. Route found, zero RoleMenuItems rows → pass through (unrestricted item).
    ///  6. Route found, has RoleMenuItems → allow only if user's role is listed.
    /// </summary>
    public class MenuAuthorizationMiddleware
    {
        private readonly RequestDelegate _next;

        private static readonly string[] _alwaysAllowed =
        {
            "/identity/",
            "/favicon",
            "/css/",
            "/js/",
            "/lib/",
            "/cust_theme_comp/",
            "/images/",
            "/fonts/",
            "/api/",
            "/home/error",
            "/usersettings/"   // theme / sidebar preference endpoints
        };

        public MenuAuthorizationMiddleware(RequestDelegate next) => _next = next;

        public async Task InvokeAsync(
            HttpContext           context,
            IMenuService          menuService,
            UserManager<ApplicationUser>  userManager,
            RoleManager<IdentityRole>     roleManager)
        {
            var path = context.Request.Path.Value?.ToLower() ?? "/";

            // Rule 1 — always-allowed paths
            if (_alwaysAllowed.Any(p => path.StartsWith(p)))
            {
                await _next(context);
                return;
            }

            // Rule 2 — not authenticated
            if (context.User?.Identity?.IsAuthenticated != true)
            {
                await _next(context);
                return;
            }

            // Rule 3 — Admin bypass (Admins can access everything)
            if (context.User.IsInRole("Admin"))
            {
                await _next(context);
                return;
            }

            // Parse route values — if routing hasn't run yet they'll be empty
            var routeData  = context.GetRouteData();
            var controller = routeData?.Values["controller"]?.ToString() ?? "";
            var action     = routeData?.Values["action"]?.ToString()     ?? "Index";
            var area       = routeData?.Values["area"]?.ToString()       ?? "";

            // Rule 4 — no controller resolved (static content, Razor Pages, etc.)
            if (string.IsNullOrEmpty(controller))
            {
                await _next(context);
                return;
            }

            // Resolve role GUIDs from the user's role names
            var user = await userManager.GetUserAsync(context.User);
            if (user == null)
            {
                await _next(context);
                return;
            }

            var roleNames = await userManager.GetRolesAsync(user);
            var roleIds   = new List<string>();
            foreach (var rn in roleNames)
            {
                var role = await roleManager.FindByNameAsync(rn);
                if (role != null) roleIds.Add(role.Id);
            }

            // Rules 4–6 delegated to MenuService
            bool allowed = await menuService.IsRouteAllowedAsync(area, controller, action, roleIds);

            if (!allowed)
            {
                context.Response.Redirect("/Identity/Account/AccessDenied");
                return;
            }

            await _next(context);
        }
    }

    public static class MenuAuthorizationMiddlewareExtensions
    {
        public static IApplicationBuilder UseMenuAuthorization(this IApplicationBuilder app)
            => app.UseMiddleware<MenuAuthorizationMiddleware>();
    }
}
