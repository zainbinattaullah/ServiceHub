using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ServiceHub.Areas.HR.Models
{
    public class Store
    {
        [Key]
        public int Id { get; set; }

        [Required(ErrorMessage = "Store Code is required.")]
        [StringLength(20)]
        [Display(Name = "Store Code")]
        public string StoreCode { get; set; } = "";

        [Required(ErrorMessage = "Store Name is required.")]
        [StringLength(100)]
        [Display(Name = "Store Name")]
        public string StoreName { get; set; } = "";

        [Display(Name = "Area")]
        public int? AreaId { get; set; }

        [ForeignKey("AreaId")]
        public Area? Area { get; set; }

        [Display(Name = "Region")]
        public int? RegionId { get; set; }

        [ForeignKey("RegionId")]
        public Region? Region { get; set; }

        [Display(Name = "Active")]
        public bool IsActive { get; set; } = true;

        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime? UpdatedAt { get; set; }
    }
}
