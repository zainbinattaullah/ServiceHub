using ClosedXML.Excel;
using iTextSharp.text;
using iTextSharp.text.pdf;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ServiceHub.Areas.HR.Models;
using ServiceHub.Data;

namespace ServiceHub.Areas.HR.Controllers
{
    [Area("HR")]
    [Authorize]
    public class EmployeeDetailReportController : Controller
    {
        private readonly ServiceHubContext _db;
        private readonly ILogger<EmployeeDetailReportController> _logger;

        // An employee is flagged when more than this many DISTINCT finger indexes
        // are found across ALL machines (a human has exactly 10 fingers).
        private const int MaxAllowedUniqueFingers = 10;

        public EmployeeDetailReportController(ServiceHubContext db,
            ILogger<EmployeeDetailReportController> logger)
        {
            _db = db;
            _logger = logger;
        }

        // ================================================================
        //  GET  /HR/EmployeeDetailReport/Index
        // ================================================================
        public IActionResult Index() => View();

        // ================================================================
        //  POST /HR/EmployeeDetailReport/GetEmployees  (DataTables)
        // ================================================================
        [HttpPost]
        public async Task<IActionResult> GetEmployees()
        {
            var form = HttpContext.Request.Form;
            var draw   = form["draw"].FirstOrDefault()            ?? "1";
            int start  = int.Parse(form["start"].FirstOrDefault()  ?? "0");
            int length = int.Parse(form["length"].FirstOrDefault() ?? "25");
            var search = (form["search[value]"].FirstOrDefault()   ?? "").Trim().ToLower();

            try
            {
                // ── Step 1: Unique employees (skip null EmployeeCode) ───
                var enrollments = await _db.EmployeeEnrollments
                    .AsNoTracking()
                    .Where(e => e.EmployeeCode != null)
                    .GroupBy(e => e.EmployeeCode!)
                    .Select(g => new
                    {
                        EmpNo          = g.Key,
                        EmpName        = g.Select(x => x.EmployeeName).FirstOrDefault(n => n != null) ?? "",
                        EnrollmentDate = g.Min(x => x.CreatedAt),
                        IsActive       = g.Any(x => x.IsActive),
                        CreatedBy      = g.Select(x => x.CreatedBy).FirstOrDefault()
                    })
                    .ToListAsync();

                // ── Step 2: Biometric logs ──────────────────────────────
                var bioLogs = await _db.Employee_Biometric_Log
                    .AsNoTracking()
                    .Where(b => b.IsActive && b.EmpNo != null)
                    .ToListAsync();

                var bioByEmp = bioLogs
                    .GroupBy(b => b.EmpNo!)
                    .ToDictionary(g => g.Key, g => new
                    {
                        TotalFingers  = g.Sum(b => b.TotalFingersEnrolled),
                        UniqueFingers = g.SelectMany(b => b.FingerIndexArray).Distinct().Count(),
                        MachineCount  = g.Select(b => b.MachineIP).Distinct().Count()
                    });

                // ── Step 3: Last attendance per employee ────────────────
                var lastAttendance = await _db.HR_Swap_Record
                    .AsNoTracking()
                    .Where(r => r.Emp_No != null && r.Swap_Time != null)
                    .GroupBy(r => r.Emp_No!)
                    .Select(g => new { EmpNo = g.Key, LastSwap = g.Max(r => r.Swap_Time) })
                    .ToDictionaryAsync(x => x.EmpNo, x => x.LastSwap);

                // ── Step 4: Build rows ──────────────────────────────────
                var rows = enrollments.Select(e =>
                {
                    bioByEmp.TryGetValue(e.EmpNo, out var bio);
                    lastAttendance.TryGetValue(e.EmpNo, out var lastSwap);

                    int totalFingers  = bio?.TotalFingers  ?? 0;
                    int uniqueFingers = bio?.UniqueFingers ?? 0;
                    bool fraudAlert   = uniqueFingers > MaxAllowedUniqueFingers;

                    return new EmployeeReportListRow
                    {
                        EmpNo                = e.EmpNo,
                        EmpName              = e.EmpName ?? "",
                        TotalFingersEnrolled = totalFingers,
                        MachinesAssigned     = bio?.MachineCount ?? 0,
                        BiometricStatus      = totalFingers > 0 ? "Active" : "Not Enrolled",
                        EnrollmentDate       = e.EnrollmentDate,
                        LastAttendance       = lastSwap,
                        IsActive             = e.IsActive,
                        FraudAlert           = fraudAlert,
                        FraudReason          = fraudAlert
                            ? $"Suspicious: {uniqueFingers} distinct finger indexes detected (max 10 possible)"
                            : null
                    };
                }).ToList();

                // ── Step 5: Search filter ───────────────────────────────
                if (!string.IsNullOrEmpty(search))
                {
                    rows = rows.Where(r =>
                        (r.EmpNo   ?? "").ToLower().Contains(search) ||
                        (r.EmpName ?? "").ToLower().Contains(search) ||
                        (r.BiometricStatus ?? "").ToLower().Contains(search)).ToList();
                }

                int total = rows.Count;
                var page = rows
                    .OrderBy(r => r.EmpNo)
                    .Skip(start).Take(length)
                    .Select(r => new
                    {
                        empNo                = r.EmpNo ?? "",
                        empName              = r.EmpName ?? "",
                        totalFingersEnrolled = r.TotalFingersEnrolled,
                        machinesAssigned     = r.MachinesAssigned,
                        biometricStatus      = r.BiometricStatus ?? "",
                        enrollmentDate       = r.EnrollmentDate?.ToString("dd-MMM-yyyy") ?? "",
                        lastAttendance       = r.LastAttendance?.ToString("dd-MMM-yyyy HH:mm") ?? "",
                        isActive             = r.IsActive,
                        fraudAlert           = r.FraudAlert,
                        fraudReason          = r.FraudReason ?? ""
                    }).ToList();

                return Json(new { draw, recordsTotal = total, recordsFiltered = total, data = page });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetEmployees failed");
                return Json(new { draw, recordsTotal = 0, recordsFiltered = 0, data = Array.Empty<object>(),
                    error = "Failed to load employees: " + ex.Message });
            }
        }

        // ================================================================
        //  GET  /HR/EmployeeDetailReport/Detail/{empNo}
        // ================================================================
        [HttpGet]
        public async Task<IActionResult> Detail(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
                return BadRequest("Employee ID is required.");

            var vm = await BuildDetailViewModelAsync(id);
            if (vm == null)
                return NotFound($"Employee '{id}' not found.");

            return View(vm);
        }

        // ================================================================
        //  GET  /HR/EmployeeDetailReport/ExportExcel?empNo=...
        // ================================================================
        [HttpGet]
        public async Task<IActionResult> ExportExcel(string empNo)
        {
            if (string.IsNullOrWhiteSpace(empNo))
                return BadRequest();

            var vm = await BuildDetailViewModelAsync(empNo);
            if (vm == null) return NotFound();

            using var wb = new XLWorkbook();

            // ── Sheet 1: Employee Profile ───────────────────────────────
            var ws = wb.Worksheets.Add("Employee Profile");
            ws.Style.Font.FontSize = 10;

            // Title
            ws.Cell(1, 1).Value = $"Employee Detail Report — {vm.EmpNo}";
            ws.Range(1, 1, 1, 4).Merge().Style.Font.Bold = true;
            ws.Cell(1, 1).Style.Font.FontSize = 14;

            ws.Cell(2, 1).Value = $"Generated: {DateTime.Now:dd-MMM-yyyy HH:mm}";
            ws.Range(2, 1, 2, 4).Merge();

            int row = 4;

            // Basic Information
            void AddSection(string title)
            {
                ws.Cell(row, 1).Value = title;
                ws.Range(row, 1, row, 4).Merge()
                    .Style.Fill.BackgroundColor = XLColor.DarkSlateBlue;
                ws.Cell(row, 1).Style.Font.FontColor = XLColor.White;
                ws.Cell(row, 1).Style.Font.Bold = true;
                row++;
            }

            void AddRow(string label, string? value)
            {
                ws.Cell(row, 1).Value = label;
                ws.Cell(row, 1).Style.Font.Bold = true;
                ws.Cell(row, 2).Value = value ?? "N/A";
                ws.Range(row, 2, row, 4).Merge();
                row++;
            }

            AddSection("BASIC INFORMATION");
            AddRow("Employee ID", vm.EmpNo);
            AddRow("Name", vm.EmpName);
            AddRow("Department", vm.Department);
            AddRow("Designation", vm.Designation);
            AddRow("Branch", vm.Branch);

            row++;
            AddSection("BIOMETRIC INFORMATION");
            AddRow("Total Fingers Enrolled", vm.TotalFingersEnrolled.ToString());
            AddRow("Unique Finger Indexes", vm.UniqueFingersAcrossAllMachines.ToString());
            AddRow("Biometric Status", vm.BiometricStatus);
            AddRow("Password Set", vm.PasswordSet ? "Yes" : "No");
            AddRow("Machines Assigned", vm.AssignedMachines.Count.ToString());

            row++;
            AddSection("SYSTEM INFORMATION");
            AddRow("Enrollment Date", vm.EnrollmentDate?.ToString("dd-MMM-yyyy HH:mm") ?? "N/A");
            AddRow("Last Attendance", vm.LastAttendance?.ToString("dd-MMM-yyyy HH:mm") ?? "Never");
            AddRow("Last Attendance Machine", vm.LastAttendanceMachine ?? "N/A");
            AddRow("Status", vm.IsActive ? "Active" : "Inactive");
            AddRow("Created By", vm.CreatedBy ?? "N/A");

            if (vm.FraudAlert)
            {
                row++;
                ws.Cell(row, 1).Value = "⚠ FRAUD ALERT";
                ws.Range(row, 1, row, 4).Merge()
                    .Style.Fill.BackgroundColor = XLColor.Red;
                ws.Cell(row, 1).Style.Font.FontColor = XLColor.White;
                ws.Cell(row, 1).Style.Font.Bold = true;
                row++;
                ws.Cell(row, 1).Value = vm.FraudReason;
                ws.Range(row, 1, row, 4).Merge();
                ws.Cell(row, 1).Style.Font.Bold = true;
                row++;
            }

            // ── Sheet 2: Machine Breakdown ──────────────────────────────
            if (vm.AssignedMachines.Any())
            {
                var ws2 = wb.Worksheets.Add("Machine Fingerprints");
                var hdrs2 = new[] { "Machine Name", "IP Address", "Fingers Enrolled", "Finger Indexes", "Finger Names", "Enrollment Date", "Status" };
                for (int c = 0; c < hdrs2.Length; c++)
                {
                    ws2.Cell(1, c + 1).Value = hdrs2[c];
                    ws2.Cell(1, c + 1).Style.Font.Bold = true;
                    ws2.Cell(1, c + 1).Style.Fill.BackgroundColor = XLColor.DarkSlateBlue;
                    ws2.Cell(1, c + 1).Style.Font.FontColor = XLColor.White;
                }
                int r2 = 2;
                foreach (var m in vm.AssignedMachines)
                {
                    ws2.Cell(r2, 1).Value = m.MachineName;
                    ws2.Cell(r2, 2).Value = m.MachineIP;
                    ws2.Cell(r2, 3).Value = m.FingersEnrolled;
                    ws2.Cell(r2, 4).Value = m.FingerIndexes;
                    ws2.Cell(r2, 5).Value = m.FingerNames;
                    ws2.Cell(r2, 6).Value = m.EnrollmentDate?.ToString("dd-MMM-yyyy HH:mm") ?? "N/A";
                    ws2.Cell(r2, 7).Value = m.IsActive ? "Active" : "Inactive";
                    r2++;
                }
                ws2.Columns().AdjustToContents();
            }

            // ── Sheet 3: Recent Attendance ──────────────────────────────
            if (vm.RecentAttendance.Any())
            {
                var ws3 = wb.Worksheets.Add("Recent Attendance");
                var hdrs3 = new[] { "Date & Time", "Machine IP", "Machine Name", "Direction" };
                for (int c = 0; c < hdrs3.Length; c++)
                {
                    ws3.Cell(1, c + 1).Value = hdrs3[c];
                    ws3.Cell(1, c + 1).Style.Font.Bold = true;
                    ws3.Cell(1, c + 1).Style.Fill.BackgroundColor = XLColor.DarkSlateBlue;
                    ws3.Cell(1, c + 1).Style.Font.FontColor = XLColor.White;
                }
                int r3 = 2;
                foreach (var p in vm.RecentAttendance)
                {
                    ws3.Cell(r3, 1).Value = p.SwapTime?.ToString("dd-MMM-yyyy HH:mm:ss") ?? "";
                    ws3.Cell(r3, 2).Value = p.MachineIP;
                    ws3.Cell(r3, 3).Value = p.MachineName;
                    ws3.Cell(r3, 4).Value = p.Direction;
                    r3++;
                }
                ws3.Columns().AdjustToContents();
            }

            ws.Columns().AdjustToContents();

            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            return File(ms.ToArray(),
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                $"Employee_{vm.EmpNo}_Report_{DateTime.Now:yyyyMMdd}.xlsx");
        }

        // ================================================================
        //  GET  /HR/EmployeeDetailReport/ExportPdf?empNo=...
        // ================================================================
        [HttpGet]
        public async Task<IActionResult> ExportPdf(string empNo)
        {
            if (string.IsNullOrWhiteSpace(empNo)) return BadRequest();

            var vm = await BuildDetailViewModelAsync(empNo);
            if (vm == null) return NotFound();

            using var ms = new MemoryStream();
            var doc = new Document(PageSize.A4, 36, 36, 54, 36);
            PdfWriter.GetInstance(doc, ms);
            doc.Open();

            // Fonts
            var titleFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 16, new BaseColor(13, 110, 253));
            var secFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 10, BaseColor.WHITE);
            var lblFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 9, BaseColor.DARK_GRAY);
            var valFont = FontFactory.GetFont(FontFactory.HELVETICA, 9, BaseColor.BLACK);
            var alertFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 10, BaseColor.WHITE);
            var tblHdrFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 8, BaseColor.WHITE);
            var tblCellFont = FontFactory.GetFont(FontFactory.HELVETICA, 8, BaseColor.BLACK);

            var secBg = new BaseColor(13, 110, 253);
            var altRow = new BaseColor(240, 248, 255);
            var fraudBg = new BaseColor(220, 53, 69);

            // Title
            doc.Add(new Paragraph($"Employee Detail Report", titleFont) { SpacingAfter = 4 });
            doc.Add(new Paragraph($"Employee ID: {vm.EmpNo}  |  Generated: {DateTime.Now:dd-MMM-yyyy HH:mm}",
                FontFactory.GetFont(FontFactory.HELVETICA, 8, BaseColor.GRAY)) { SpacingAfter = 12 });

            // Fraud alert banner
            if (vm.FraudAlert)
            {
                var alertTbl = new PdfPTable(1) { WidthPercentage = 100, SpacingAfter = 10 };
                var ac = new PdfPCell(new Phrase($"⚠ FRAUD ALERT: {vm.FraudReason}", alertFont))
                {
                    BackgroundColor = fraudBg, Padding = 8,
                    HorizontalAlignment = Element.ALIGN_CENTER
                };
                alertTbl.AddCell(ac);
                doc.Add(alertTbl);
            }

            // ── Helper: add a 2-column info section ────────────────────
            void AddInfoTable(string sectionTitle, List<(string Label, string Value)> items)
            {
                var t = new PdfPTable(2) { WidthPercentage = 100, SpacingAfter = 10 };
                t.SetWidths(new float[] { 35, 65 });

                // Section header
                var hdr = new PdfPCell(new Phrase(sectionTitle, secFont))
                {
                    Colspan = 2, BackgroundColor = secBg,
                    Padding = 6, Border = Rectangle.NO_BORDER
                };
                t.AddCell(hdr);

                bool alt = false;
                foreach (var (lbl, val) in items)
                {
                    var bg = alt ? altRow : BaseColor.WHITE;
                    t.AddCell(new PdfPCell(new Phrase(lbl, lblFont))
                    { Padding = 4, BackgroundColor = bg, Border = Rectangle.BOX });
                    t.AddCell(new PdfPCell(new Phrase(val, valFont))
                    { Padding = 4, BackgroundColor = bg, Border = Rectangle.BOX });
                    alt = !alt;
                }
                doc.Add(t);
            }

            AddInfoTable("BASIC INFORMATION", new()
            {
                ("Employee ID", vm.EmpNo),
                ("Name", vm.EmpName),
                ("Department", vm.Department),
                ("Designation", vm.Designation),
                ("Branch", vm.Branch)
            });

            AddInfoTable("BIOMETRIC INFORMATION", new()
            {
                ("Total Fingers Enrolled", vm.TotalFingersEnrolled.ToString()),
                ("Unique Finger Indexes", vm.UniqueFingersAcrossAllMachines.ToString()),
                ("Biometric Status", vm.BiometricStatus),
                ("Password Set", vm.PasswordSet ? "Yes" : "No"),
                ("Machines Assigned", vm.AssignedMachines.Count.ToString())
            });

            AddInfoTable("SYSTEM INFORMATION", new()
            {
                ("Enrollment Date", vm.EnrollmentDate?.ToString("dd-MMM-yyyy HH:mm") ?? "N/A"),
                ("Last Attendance", vm.LastAttendance?.ToString("dd-MMM-yyyy HH:mm") ?? "Never"),
                ("Last Machine", vm.LastAttendanceMachine ?? "N/A"),
                ("Status", vm.IsActive ? "Active" : "Inactive"),
                ("Created By", vm.CreatedBy ?? "N/A")
            });

            // ── Machine Fingerprint Breakdown table ─────────────────────
            if (vm.AssignedMachines.Any())
            {
                var mt = new PdfPTable(5) { WidthPercentage = 100, SpacingAfter = 10 };
                mt.SetWidths(new float[] { 22, 16, 10, 28, 14 });

                // Header row
                var mhdr = new PdfPCell(new Phrase("MACHINE FINGERPRINT BREAKDOWN", secFont))
                {
                    Colspan = 5, BackgroundColor = secBg,
                    Padding = 6, Border = Rectangle.NO_BORDER
                };
                mt.AddCell(mhdr);

                foreach (var h in new[] { "Machine", "IP", "Fingers", "Finger Names", "Status" })
                {
                    mt.AddCell(new PdfPCell(new Phrase(h, tblHdrFont))
                    {
                        BackgroundColor = new BaseColor(52, 58, 64),
                        Padding = 4, HorizontalAlignment = Element.ALIGN_CENTER
                    });
                }

                bool alt = false;
                foreach (var m in vm.AssignedMachines)
                {
                    var bg = alt ? altRow : BaseColor.WHITE;
                    mt.AddCell(new PdfPCell(new Phrase(m.MachineName, tblCellFont)) { Padding = 3, BackgroundColor = bg });
                    mt.AddCell(new PdfPCell(new Phrase(m.MachineIP, tblCellFont)) { Padding = 3, BackgroundColor = bg });
                    mt.AddCell(new PdfPCell(new Phrase(m.FingersEnrolled.ToString(), tblCellFont))
                    { Padding = 3, BackgroundColor = bg, HorizontalAlignment = Element.ALIGN_CENTER });
                    mt.AddCell(new PdfPCell(new Phrase(m.FingerNames, tblCellFont)) { Padding = 3, BackgroundColor = bg });
                    mt.AddCell(new PdfPCell(new Phrase(m.IsActive ? "Active" : "Inactive", tblCellFont))
                    { Padding = 3, BackgroundColor = bg, HorizontalAlignment = Element.ALIGN_CENTER });
                    alt = !alt;
                }
                doc.Add(mt);
            }

            // ── Recent Attendance table ─────────────────────────────────
            if (vm.RecentAttendance.Any())
            {
                var at = new PdfPTable(4) { WidthPercentage = 100, SpacingAfter = 10 };
                at.SetWidths(new float[] { 30, 20, 30, 20 });

                var ahdr = new PdfPCell(new Phrase("RECENT ATTENDANCE (LAST 10 PUNCHES)", secFont))
                {
                    Colspan = 4, BackgroundColor = secBg,
                    Padding = 6, Border = Rectangle.NO_BORDER
                };
                at.AddCell(ahdr);

                foreach (var h in new[] { "Date & Time", "Machine IP", "Machine Name", "Direction" })
                {
                    at.AddCell(new PdfPCell(new Phrase(h, tblHdrFont))
                    {
                        BackgroundColor = new BaseColor(52, 58, 64),
                        Padding = 4, HorizontalAlignment = Element.ALIGN_CENTER
                    });
                }

                bool alt = false;
                foreach (var p in vm.RecentAttendance)
                {
                    var bg = alt ? altRow : BaseColor.WHITE;
                    at.AddCell(new PdfPCell(new Phrase(p.SwapTime?.ToString("dd-MMM-yyyy HH:mm") ?? "", tblCellFont)) { Padding = 3, BackgroundColor = bg });
                    at.AddCell(new PdfPCell(new Phrase(p.MachineIP ?? "", tblCellFont)) { Padding = 3, BackgroundColor = bg });
                    at.AddCell(new PdfPCell(new Phrase(p.MachineName ?? "", tblCellFont)) { Padding = 3, BackgroundColor = bg });
                    at.AddCell(new PdfPCell(new Phrase(p.Direction, tblCellFont))
                    { Padding = 3, BackgroundColor = bg, HorizontalAlignment = Element.ALIGN_CENTER });
                    alt = !alt;
                }
                doc.Add(at);
            }

            doc.Close();
            return File(ms.ToArray(), "application/pdf",
                $"Employee_{vm.EmpNo}_Report_{DateTime.Now:yyyyMMdd}.pdf");
        }

        // ================================================================
        //  GET  /HR/EmployeeDetailReport/ExportListExcel
        //  Exports the full employee list with fraud flags
        // ================================================================
        [HttpGet]
        public async Task<IActionResult> ExportListExcel(string? search = null)
        {
            // Rebuild the list (same logic as GetEmployees but no pagination)
            var enrollments = await _db.EmployeeEnrollments
                .AsNoTracking()
                .GroupBy(e => e.EmployeeCode)
                .Select(g => new
                {
                    EmpNo = g.Key,
                    EmpName = g.Select(x => x.EmployeeName).FirstOrDefault(n => n != null) ?? "",
                    EnrollmentDate = g.Min(x => x.CreatedAt),
                    IsActive = g.Any(x => x.IsActive)
                })
                .ToListAsync();

            var bioLogs = await _db.Employee_Biometric_Log
                .AsNoTracking()
                .Where(b => b.IsActive && b.EmpNo != null)
                .ToListAsync();

            var bioByEmp = bioLogs
                .GroupBy(b => b.EmpNo!)
                .ToDictionary(g => g.Key, g => new
                {
                    TotalFingers = g.Sum(b => b.TotalFingersEnrolled),
                    UniqueFingers = g.SelectMany(b => b.FingerIndexArray).Distinct().Count(),
                    MachineCount = g.Select(b => b.MachineIP).Distinct().Count()
                });

            var lastAttendance = await _db.HR_Swap_Record
                .AsNoTracking()
                .Where(r => r.Emp_No != null && r.Swap_Time != null)
                .GroupBy(r => r.Emp_No!)
                .Select(g => new { EmpNo = g.Key, LastSwap = g.Max(r => r.Swap_Time) })
                .ToDictionaryAsync(x => x.EmpNo, x => x.LastSwap);

            var rows = enrollments.Select(e =>
            {
                bioByEmp.TryGetValue(e.EmpNo, out var bio);
                lastAttendance.TryGetValue(e.EmpNo, out var lastSwap);
                int uniqueFingers = bio?.UniqueFingers ?? 0;
                bool fraud = uniqueFingers > MaxAllowedUniqueFingers;
                return new EmployeeReportListRow
                {
                    EmpNo = e.EmpNo,
                    EmpName = e.EmpName ?? "",
                    TotalFingersEnrolled = bio?.TotalFingers ?? 0,
                    MachinesAssigned = bio?.MachineCount ?? 0,
                    BiometricStatus = (bio?.TotalFingers ?? 0) > 0 ? "Active" : "Not Enrolled",
                    EnrollmentDate = e.EnrollmentDate,
                    LastAttendance = lastSwap,
                    IsActive = e.IsActive,
                    FraudAlert = fraud,
                    FraudReason = fraud
                        ? $"{uniqueFingers} distinct finger indexes detected"
                        : null
                };
            }).ToList();

            if (!string.IsNullOrWhiteSpace(search))
            {
                var sv = search.ToLower();
                rows = rows.Where(r =>
                    r.EmpNo.ToLower().Contains(sv) ||
                    r.EmpName.ToLower().Contains(sv)).ToList();
            }

            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add("Employee Report");

            var headers = new[] { "#", "Employee ID", "Name", "Biometric Status",
                "Total Fingers", "Machines Assigned", "Enrollment Date",
                "Last Attendance", "Status", "Fraud Alert", "Fraud Reason" };

            for (int c = 0; c < headers.Length; c++)
            {
                ws.Cell(1, c + 1).Value = headers[c];
                ws.Cell(1, c + 1).Style.Font.Bold = true;
                ws.Cell(1, c + 1).Style.Fill.BackgroundColor = XLColor.DarkSlateBlue;
                ws.Cell(1, c + 1).Style.Font.FontColor = XLColor.White;
            }

            int row = 2;
            foreach (var r in rows.OrderBy(x => x.EmpNo))
            {
                ws.Cell(row, 1).Value = row - 1;
                ws.Cell(row, 2).Value = r.EmpNo;
                ws.Cell(row, 3).Value = r.EmpName;
                ws.Cell(row, 4).Value = r.BiometricStatus;
                ws.Cell(row, 5).Value = r.TotalFingersEnrolled;
                ws.Cell(row, 6).Value = r.MachinesAssigned;
                ws.Cell(row, 7).Value = r.EnrollmentDate?.ToString("dd-MMM-yyyy") ?? "";
                ws.Cell(row, 8).Value = r.LastAttendance?.ToString("dd-MMM-yyyy HH:mm") ?? "Never";
                ws.Cell(row, 9).Value = r.IsActive ? "Active" : "Inactive";
                ws.Cell(row, 10).Value = r.FraudAlert ? "YES" : "No";
                ws.Cell(row, 11).Value = r.FraudReason ?? "";

                if (r.FraudAlert)
                    ws.Range(row, 1, row, headers.Length).Style.Fill.BackgroundColor = XLColor.LightSalmon;

                row++;
            }

            ws.Columns().AdjustToContents();

            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            return File(ms.ToArray(),
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                $"EmployeeReport_{DateTime.Now:yyyyMMdd}.xlsx");
        }

        // ================================================================
        //  PRIVATE HELPER — build full detail view model for one employee
        // ================================================================
        private async Task<EmployeeDetailViewModel?> BuildDetailViewModelAsync(string empNo)
        {
            // ── Enrollments for this employee ───────────────────────────
            var enrollments = await _db.EmployeeEnrollments
                .AsNoTracking()
                .Where(e => e.EmployeeCode == empNo)
                .OrderBy(e => e.CreatedAt)
                .ToListAsync();

            if (!enrollments.Any()) return null;

            var first = enrollments.First();

            // ── Biometric logs (all machines) ───────────────────────────
            var bioLogs = await _db.Employee_Biometric_Log
                .AsNoTracking()
                .Where(b => b.EmpNo == empNo && b.IsActive)
                .OrderByDescending(b => b.EnrollmentDate)
                .ToListAsync();

            // Unique finger indexes across ALL machines
            var allFingerIndexes = bioLogs
                .SelectMany(b => b.FingerIndexArray)
                .Distinct()
                .OrderBy(x => x)
                .ToList();
            int totalFingers = bioLogs.Sum(b => b.TotalFingersEnrolled);
            int uniqueFingers = allFingerIndexes.Count;

            // ── Machine lookup ──────────────────────────────────────────
            var machineIPs = enrollments.Select(e => e.MachineIP).Distinct().ToList();
            var machines = await _db.AttendenceMachines
                .AsNoTracking()
                .Where(m => machineIPs.Contains(m.IpAddress))
                .ToListAsync();
            var machineDict = machines.ToDictionary(m => m.IpAddress ?? "", m => m);

            // ── Branch / Department from Store ─────────────────────────
            var locationCodes = machines.Select(m => m.Location).Distinct().Where(l => l != null).ToList();
            var stores = await _db.Stores
                .AsNoTracking()
                .Where(s => locationCodes.Contains(s.StoreCode))
                .ToListAsync();
            var primaryLocation = machines.FirstOrDefault()?.Location;
            var store = stores.FirstOrDefault(s => s.StoreCode == primaryLocation);

            // ── Last attendance ─────────────────────────────────────────
            var lastRecord = await _db.HR_Swap_Record
                .AsNoTracking()
                .Where(r => r.Emp_No == empNo && r.Swap_Time != null)
                .OrderByDescending(r => r.Swap_Time)
                .FirstOrDefaultAsync();

            // ── Recent attendance (last 10) ─────────────────────────────
            var recentPunches = await _db.HR_Swap_Record
                .AsNoTracking()
                .Where(r => r.Emp_No == empNo && r.Swap_Time != null)
                .OrderByDescending(r => r.Swap_Time)
                .Take(10)
                .ToListAsync();

            // ── Assigned machines with biometric detail ─────────────────
            var assignedMachines = new List<MachineEnrollmentDetail>();
            foreach (var enrl in enrollments.GroupBy(e => e.MachineIP).Select(g => g.First()))
            {
                if (string.IsNullOrEmpty(enrl.MachineIP)) continue;
                machineDict.TryGetValue(enrl.MachineIP, out var mach);
                var bio = bioLogs.FirstOrDefault(b => b.MachineIP == enrl.MachineIP);
                var fingerNames = bio?.FingerIndexArray
                    .Select(fi => Employee_Biometric_Log.FingerName(fi))
                    .ToList() ?? new List<string>();

                assignedMachines.Add(new MachineEnrollmentDetail
                {
                    MachineId = mach?.Id ?? 0,
                    MachineIP = enrl.MachineIP,
                    MachineName = mach?.Name ?? enrl.MachineIP,
                    FingersEnrolled = bio?.TotalFingersEnrolled ?? 0,
                    FingerIndexes = bio?.EnrolledFingerIndexes ?? "—",
                    FingerNames = fingerNames.Any() ? string.Join(", ", fingerNames) : "—",
                    EnrollmentDate = bio?.EnrollmentDate ?? enrl.CreatedAt,
                    IsActive = bio?.IsActive ?? enrl.IsActive
                });
            }

            // ── Fraud detection ─────────────────────────────────────────
            bool fraudAlert = uniqueFingers > MaxAllowedUniqueFingers;
            string? fraudReason = fraudAlert
                ? $"Suspicious: {uniqueFingers} distinct finger indexes detected across machines (maximum for one person is 10)"
                : null;

            // ── Last attendance machine name ────────────────────────────
            string? lastMachine = null;
            if (lastRecord?.Machine_IP != null)
            {
                machineDict.TryGetValue(lastRecord.Machine_IP, out var lm);
                lastMachine = lm?.Name ?? lastRecord.Machine_IP;
            }

            // ── Recent punch rows ───────────────────────────────────────
            var recentRows = recentPunches.Select(p =>
            {
                machineDict.TryGetValue(p.Machine_IP ?? "", out var pm);
                string dir = p.Shift_In == true ? "In" : p.Shift_Out == true ? "Out" : "–";
                return new AttendancePunchRow
                {
                    SwapTime = p.Swap_Time,
                    MachineIP = p.Machine_IP,
                    MachineName = pm?.Name ?? p.Machine_IP,
                    Direction = dir
                };
            }).ToList();

            return new EmployeeDetailViewModel
            {
                EmpNo = empNo,
                EmpName = first.EmployeeName ?? "",
                Department = store?.Department ?? "N/A",
                Designation = "N/A",            // not stored in current schema
                Branch = store?.StoreName ?? "N/A",

                TotalFingersEnrolled = totalFingers,
                UniqueFingersAcrossAllMachines = uniqueFingers,
                BiometricStatus = totalFingers > 0 ? "Active" : "Not Enrolled",
                PasswordSet = false,            // not stored in current schema
                AssignedMachines = assignedMachines,

                EnrollmentDate = first.CreatedAt,
                LastAttendance = lastRecord?.Swap_Time,
                LastAttendanceMachine = lastMachine,
                IsActive = first.IsActive,
                CreatedBy = first.CreatedBy,

                FraudAlert = fraudAlert,
                FraudReason = fraudReason,
                RecentAttendance = recentRows
            };
        }
    }
}
