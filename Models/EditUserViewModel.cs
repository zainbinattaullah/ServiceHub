using System.ComponentModel.DataAnnotations;

namespace ServiceHub.Models
{
    public class EditUserViewModel
    {
        public string Id { get; set; }

        [Required]
        [Display(Name = "First Name")]
        public string FirstName { get; set; }

        [Required]
        [Display(Name = "Last Name")]
        public string LastName { get; set; }

        [EmailAddress]
        public string Email { get; set; }

        public List<string> AllRoles { get; set; } = new();
        public List<string> SelectedRoles { get; set; } = new();
    }
}
