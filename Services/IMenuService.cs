using ServiceHub.Areas.HR.Models;

namespace ServiceHub.Services
{
    /// <summary>
    /// Builds and caches the navigation menu tree for a given user/role set.
    /// </summary>
    public interface IMenuService
    {
        /// <summary>
        /// Returns top-level menu nodes (with populated Children) that
        /// the supplied roles are allowed to see.
        /// An item with zero RoleMenuItems rows is visible to ALL authenticated users.
        /// </summary>
        Task<List<MenuNode>> GetMenuForRolesAsync(IEnumerable<string> roleIds);

        /// <summary>
        /// Returns every active menu item (flat list) — used by the admin management UI.
        /// </summary>
        Task<List<MenuItem>> GetAllItemsAsync();

        /// <summary>Invalidates the in-memory cache so changes take effect immediately.</summary>
        void InvalidateCache();

        /// <summary>
        /// Checks whether ANY of the supplied roles can access the given
        /// area / controller / action combination.
        /// Returns true when the route is not found in the menu (open access),
        /// or when the item has no role restrictions.
        /// Returns false only when the item exists AND none of the roles match.
        /// </summary>
        Task<bool> IsRouteAllowedAsync(string? area, string controller,
                                       string action, IEnumerable<string> roleIds);
    }

    /// <summary>
    /// Lightweight tree node passed to the Razor view component.
    /// Contains only what the view needs — no EF navigation properties.
    /// </summary>
    public class MenuNode
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Icon { get; set; }
        public string ResolvedUrl { get; set; } = "#";
        public bool IsGroup { get; set; }           // true = folder/parent item
        public List<MenuNode> Children { get; set; } = new();
        public int OrderIndex { get; set; }
    }
}
