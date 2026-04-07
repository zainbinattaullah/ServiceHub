using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ServiceHub.Areas.HR.Models
{
    public class Employee_Biometric_Log
    {
        [Key]
        public int Id { get; set; }

        [Column("Emp_No")]
        [StringLength(50)]
        public string? EmpNo { get; set; }

        [Column("Emp_Name")]
        [StringLength(250)]
        public string? EmpName { get; set; }

        [StringLength(200)]
        public string? MachineName { get; set; }

        [StringLength(50)]
        public string? MachineIP { get; set; }

        /// <summary>Comma-separated finger indexes, e.g. "0,1,2"</summary>
        [StringLength(100)]
        public string? EnrolledFingerIndexes { get; set; }

        public int TotalFingersEnrolled { get; set; }

        public DateTime EnrollmentDate { get; set; }

        public DateTime? LastUpdated { get; set; }

        public bool IsActive { get; set; } = true;

        /// <summary>Parsed finger index array (not mapped to DB column).</summary>
        [NotMapped]
        public int[] FingerIndexArray =>
            string.IsNullOrWhiteSpace(EnrolledFingerIndexes)
                ? Array.Empty<int>()
                : EnrolledFingerIndexes
                    .Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => int.TryParse(x.Trim(), out int v) ? v : -1)
                    .Where(v => v >= 0)
                    .ToArray();

        /// <summary>Human-readable finger name (Right Thumb = index 0, etc.).</summary>
        public static string FingerName(int index) => index switch
        {
            0 => "Right Thumb",
            1 => "Right Index",
            2 => "Right Middle",
            3 => "Right Ring",
            4 => "Right Little",
            5 => "Left Thumb",
            6 => "Left Index",
            7 => "Left Middle",
            8 => "Left Ring",
            9 => "Left Little",
            _ => $"Finger {index}"
        };
    }
}
