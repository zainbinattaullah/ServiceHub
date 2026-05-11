using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ServiceHub.Areas.HR.Models
{
    public class AttendanceMachine
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "Name is required.")]
        [StringLength(250, ErrorMessage = "Name cannot exceed 250 characters.")]
        public string Name { get; set; }

        [Required(ErrorMessage = "IP Address is required.")]
        [StringLength(50, ErrorMessage = "IP Address cannot exceed 50 characters.")]
        public string IpAddress { get; set; }

        [Required(ErrorMessage = "Port is required.")]
        [Range(1, 65535, ErrorMessage = "Port must be between 1 and 65535.")]
        public int Port { get; set; }       
        public bool IsActive { get; set; } = false;       
        public bool IsFetchAll { get; set; } = false;   
        public string? Location { get; set; }
        public string? Description { get; set; }

        [StringLength(100, ErrorMessage = "Device Model cannot exceed 100 characters.")]
        public string? DeviceModel { get; set; }

        [Display(Name = "Store")]
        public int? StoreId { get; set; }

        [ForeignKey("StoreId")]
        public Store? Store { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        public DateTime? LastUpdated { get; set; }
    }
}
