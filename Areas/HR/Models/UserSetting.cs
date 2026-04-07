using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ServiceHub.Areas.HR.Models
{
    /// <summary>
    /// Stores per-user UI preferences: theme, sidebar state, etc.
    /// One row per user (upsert on save).
    /// </summary>
    [Table("UserSettings")]
    public class UserSetting
    {
        [Key]
        public int Id { get; set; }

        /// <summary>ASP.NET Identity user ID (string GUID).</summary>
        [Required, StringLength(450)]
        public string UserId { get; set; } = string.Empty;

        /// <summary>
        /// One of: light | dark | theme-blue | theme-green | theme-purple | theme-orange
        /// </summary>
        [StringLength(50)]
        public string Theme { get; set; } = "light";

        /// <summary>Whether the sidebar starts collapsed.</summary>
        public bool SidebarCollapsed { get; set; } = false;

        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
