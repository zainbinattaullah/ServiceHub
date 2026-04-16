using System.ComponentModel.DataAnnotations;

namespace ServiceHub.Areas.HR.Models
{
    public class Department
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "Code is required.")]
        [StringLength(20)]
        [Display(Name = "Department Code")]
        public string Code { get; set; } = "";

        [Required(ErrorMessage = "Name is required.")]
        [StringLength(100)]
        [Display(Name = "Department Name")]
        public string Name { get; set; } = "";

        [StringLength(255)]
        public string? Description { get; set; }

        [Display(Name = "Active")]
        public bool IsActive { get; set; } = true;

        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime? UpdatedAt { get; set; }
    }
}
