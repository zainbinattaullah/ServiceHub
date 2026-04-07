using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ServiceHub.Areas.HR.Models
{
    /// <summary>
    /// Represents a single navigation item in the dynamic menu system.
    /// Supports unlimited nesting via the self-referencing ParentId.
    /// </summary>
    [Table("MenuItems")]
    public class MenuItem
    {
        [Key]
        public int Id { get; set; }

        [Required, StringLength(100)]
        public string Name { get; set; } = string.Empty;

        /// <summary>Bootstrap Icon class, e.g. "bi bi-house-door-fill"</summary>
        [StringLength(100)]
        public string? Icon { get; set; }

        /// <summary>Absolute URL — used when Area/Controller/Action are null (external or anchor links).</summary>
        [StringLength(500)]
        public string? Url { get; set; }

        [StringLength(100)]
        public string? Area { get; set; }

        [StringLength(100)]
        public string? Controller { get; set; }

        [StringLength(100)]
        public string? Action { get; set; }

        /// <summary>Null = top-level item; otherwise references the parent group item.</summary>
        public int? ParentId { get; set; }

        /// <summary>Lower numbers appear first in the sidebar.</summary>
        public int OrderIndex { get; set; } = 0;

        public bool IsActive { get; set; } = true;

        // ── Navigation Properties ────────────────────────────────────────
        [ForeignKey(nameof(ParentId))]
        public virtual MenuItem? Parent { get; set; }

        public virtual ICollection<MenuItem> Children { get; set; } = new List<MenuItem>();

        public virtual ICollection<RoleMenuItem> RoleMenuItems { get; set; } = new List<RoleMenuItem>();

        // ── Computed helpers (not mapped) ────────────────────────────────
        [NotMapped]
        public bool IsGroup => !string.IsNullOrEmpty(Children?.Where(c => c.IsActive).Select(c => c.Id).FirstOrDefault().ToString())
                               || (string.IsNullOrEmpty(Url) && string.IsNullOrEmpty(Controller));

        [NotMapped]
        public string ResolvedUrl =>
            !string.IsNullOrWhiteSpace(Url) ? Url :
            !string.IsNullOrWhiteSpace(Controller) ?
                (string.IsNullOrWhiteSpace(Area)
                    ? $"/{Controller}/{Action ?? "Index"}"
                    : $"/{Area}/{Controller}/{Action ?? "Index"}")
                : "#";
    }
}
