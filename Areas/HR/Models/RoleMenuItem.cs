using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ServiceHub.Areas.HR.Models
{
    /// <summary>
    /// Join table — maps which roles can see which menu items.
    /// A menu item with NO rows here is visible to ALL authenticated users.
    /// Once any row exists for a MenuItemId, only those listed roles can see it.
    /// </summary>
    [Table("RoleMenuItems")]
    public class RoleMenuItem
    {
        [Key]
        public int Id { get; set; }

        /// <summary>ASP.NET Identity role ID (string GUID).</summary>
        [Required, StringLength(450)]
        public string RoleId { get; set; } = string.Empty;

        public int MenuItemId { get; set; }

        // ── Navigation ───────────────────────────────────────────────────
        [ForeignKey(nameof(MenuItemId))]
        public virtual MenuItem MenuItem { get; set; } = null!;
    }
}
