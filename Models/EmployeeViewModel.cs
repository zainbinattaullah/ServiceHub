using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;
using System.Collections.Generic;

namespace ServiceHub.Models
{
    public class EmployeeViewModel
    {
        [Required]
        [Display(Name = "Employee Code")]
        public string EmployeeCode { get; set; }

        [Required]
        [Display(Name = "Employee Name")]
        public string EmployeeName { get; set; }

        [Required]
        [Display(Name = "Machine IP")]
        public string MachineIP { get; set; }

        // List of machines for dropdown
        public IEnumerable<SelectListItem>? MachineIPs { get; set; }

        // Optional fixed privilege options; if provided, view will render a dropdown
        public IEnumerable<SelectListItem>? PrivilegeOptions { get; set; }

        [Required]
        [Display(Name = "Privilege")]
        public string Privilege { get; set; }
    }
}