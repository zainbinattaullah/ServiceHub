using ClosedXML.Excel;
using iTextSharp.text;
using iTextSharp.text.pdf;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ServiceHub.Areas.HR.Models;
using ServiceHub.Data;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace ServiceHub.Areas.HR.Controllers
{
    [Area("HR")]
    [Authorize]
    public class AttendanceReportController : Controller
    {
        private readonly ServiceHubContext _db;
        private const double StandardHours = 12.0;
        public AttendanceReportController(ServiceHubContext db)
        {
            _db = db;
        }    
        public async Task<IActionResult> Index()
        {
            var vm = new AttendanceReportPageViewModel
            {
                Departments = await GetDepartmentsAsync(),
                Locations = await GetLocationsAsync()
            };
            return View(vm);
        }
        // ---------------------------------------------------------------
        //  POST  /HR/AttendanceReport/GetReportData
        //  DataTables server-side JSON endpoint
        // ---------------------------------------------------------------
        [HttpPost]
        public async Task<IActionResult> GetReportData()
        {
            var form = HttpContext.Request.Form;
            var draw = form["draw"].FirstOrDefault() ?? "1";
            int start = int.Parse(form["start"].FirstOrDefault() ?? "0");
            int length = int.Parse(form["length"].FirstOrDefault() ?? "25");
            var filter = ExtractFilter(form);
            if (filter.ReportType == "Monthly")
            {
                var monthly = await BuildMonthlySummaryAsync(filter);
                int total = monthly.Count;
                var page = monthly.Skip(start).Take(length).ToList();
                return Json(new { draw, recordsTotal = total, recordsFiltered = total, data = page });
            }

            if (filter.ReportType == "Absentee")
            {
                var absent = await BuildAbsenteeReportAsync(filter);
                int total = absent.Count;
                var page = absent.Skip(start).Take(length).ToList();
                return Json(new { draw, recordsTotal = total, recordsFiltered = total, data = page });
            }
            // Default: Detail report
            var detail = await BuildDetailReportAsync(filter);
            int detailTotal = detail.Count;
            var detailPage = detail.Skip(start).Take(length).ToList();
            return Json(new { draw, recordsTotal = detailTotal, recordsFiltered = detailTotal, data = detailPage });
        }

        // ---------------------------------------------------------------
        //  GET  /HR/AttendanceReport/ExportExcel
        // ---------------------------------------------------------------
        [HttpGet]
        public async Task<IActionResult> ExportExcel(string reportType, string dateFrom, string dateTo,string employeeId, string employeeName, string department, string location,string month, string year)
        {
            var filter = BuildFilterFromQS(reportType, dateFrom, dateTo, employeeId, employeeName, department, location, month, year);

            using var wb = new XLWorkbook();
            if (filter.ReportType == "Monthly")
            {
                var data = await BuildMonthlySummaryAsync(filter);
                BuildMonthlyExcelSheet(wb, data);
            }
            else if (filter.ReportType == "Absentee")
            {
                var data = await BuildAbsenteeReportAsync(filter);
                BuildAbsenteeExcelSheet(wb, data);
            }
            else
            {
                var data = await BuildDetailReportAsync(filter);
                BuildDetailExcelSheet(wb, data);
            }

            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            return File(ms.ToArray(),"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",$"AttendanceReport_{filter.ReportType}_{DateTime.Now:yyyyMMdd_HHmm}.xlsx");
        }

        // ---------------------------------------------------------------
        //  GET  /HR/AttendanceReport/ExportPdf
        // ---------------------------------------------------------------
        [HttpGet]
        public async Task<IActionResult> ExportPdf(string reportType, string dateFrom, string dateTo,string employeeId, string employeeName, string department, string location, string month, string year)
        {
            var filter = BuildFilterFromQS(reportType, dateFrom, dateTo,employeeId, employeeName, department, location, month, year);

            using var ms = new MemoryStream();
            var doc = new Document(PageSize.A4.Rotate(), 15f, 15f, 25f, 25f);
            PdfWriter.GetInstance(doc, ms);
            doc.Open();

            if (filter.ReportType == "Monthly")
            {
                var data = await BuildMonthlySummaryAsync(filter);
                BuildMonthlyPdf(doc, data, filter);
            }
            else if (filter.ReportType == "Absentee")
            {
                var data = await BuildAbsenteeReportAsync(filter);
                BuildAbsenteePdf(doc, data, filter);
            }
            else
            {
                var data = await BuildDetailReportAsync(filter);
                BuildDetailPdf(doc, data, filter);
            }

            doc.Close();
            return File(ms.ToArray(), "application/pdf",
                $"AttendanceReport_{filter.ReportType}_{DateTime.Now:yyyyMMdd_HHmm}.pdf");
        }

        // ===============================================================
        //  CORE DATA BUILDERS
        // ===============================================================

        /// <summary>
        /// Detailed attendance report.
        /// Per employee per date: min punch = Check-In, max punch = Check-Out.
        /// Working hours = CheckOut - CheckIn.
        /// Late  = worked but less than StandardHours (12 h).
        /// OT    = worked more than StandardHours.
        /// </summary>
        private async Task<List<AttendanceReportViewModel>> BuildDetailReportAsync(
            AttendanceReportFilter filter)
        {
            // --- Base query ---
            var query = _db.HR_Swap_Record.Join(_db.AttendenceMachines,
                    s => s.MachineId,
                    m => m.Id,
                    (s, m) => new
                    {
                        s.Emp_No,
                        EmpName = s.Emp_Name ?? "",
                        SwapTime = s.Swap_Time ?? DateTime.MinValue,
                        Date = s.Swap_Time.HasValue ? s.Swap_Time.Value.Date : DateTime.MinValue,
                        Location = m.Location,   // adjust property name to match your entity
                        LocationId = m.Id,
                        Department = (string)null  // populate if you have a Department column
                    });

            // --- Filters ---
            if (filter.DateFrom.HasValue)
                query = query.Where(x => x.Date >= filter.DateFrom.Value.Date);

            if (filter.DateTo.HasValue)
                query = query.Where(x => x.Date <= filter.DateTo.Value.Date);

            if (!string.IsNullOrWhiteSpace(filter.EmployeeId))
                query = query.Where(x => x.Emp_No.Contains(filter.EmployeeId));

            if (!string.IsNullOrWhiteSpace(filter.EmployeeName))
                query = query.Where(x => x.EmpName.Contains(filter.EmployeeName));

            if (!string.IsNullOrWhiteSpace(filter.Location))
                query = query.Where(x => x.Location.Contains(filter.Location));

            if (!string.IsNullOrWhiteSpace(filter.Department))
                query = query.Where(x => x.Department != null &&
                                         x.Department.Contains(filter.Department));

            // --- Group by employee + date, compute min/max ---
            var grouped = await query
                .GroupBy(x => new { x.Emp_No, x.EmpName, x.Date, x.Location, x.Department })
                .Select(g => new
                {
                    g.Key.Emp_No,
                    g.Key.EmpName,
                    g.Key.Date,
                    g.Key.Location,
                    g.Key.Department,
                    CheckIn = g.Min(x => x.SwapTime),
                    CheckOut = g.Max(x => x.SwapTime),
                    PunchCount = g.Count()
                })
                .OrderBy(x => x.Date)
                .ThenBy(x => x.Emp_No)
                .ToListAsync();

            var rows = new List<AttendanceReportViewModel>();

            foreach (var g in grouped)
            {
                double? workedHours = null;
                string hoursLabel = "N/A";
                bool isLate = false;
                double? overtime = null;
                string otLabel = "—";
                // Only calculate hours if there are at least 2 punches
                // (same min/max with 1 punch means the person only checked in)
                if (g.PunchCount >= 2 && g.CheckIn != g.CheckOut)
                {
                    var span = g.CheckOut - g.CheckIn;
                    workedHours = span.TotalHours;
                    hoursLabel = FormatHours(span);

                    if (workedHours < StandardHours)
                    {
                        isLate = true;
                    }
                    else if (workedHours > StandardHours)
                    {
                        var otSpan = TimeSpan.FromHours(workedHours.Value - StandardHours);
                        overtime = otSpan.TotalHours;
                        otLabel = FormatHours(otSpan);
                    }
                }
                else if (g.PunchCount == 1)
                {
                    // Only one punch — show Check-In, no Check-Out
                    hoursLabel = "Single punch";
                    isLate = true;
                }

                rows.Add(new AttendanceReportViewModel
                {
                    EmployeeId = g.Emp_No,
                    EmployeeName = g.EmpName,
                    Department = g.Department ?? "N/A",
                    Location = g.Location ?? "N/A",
                    Date = g.Date,
                    CheckIn = g.CheckIn,
                    CheckOut = g.PunchCount >= 2 && g.CheckIn != g.CheckOut ? g.CheckOut : (DateTime?)null,
                    TotalWorkingHours = workedHours,
                    TotalHoursLabel = hoursLabel,
                    IsLate = isLate,
                    OvertimeHours = overtime,
                    OvertimeLabel = otLabel,
                    IsAbsent = false
                });
            }

            return rows;
        }

        /// <summary>
        /// Monthly summary: aggregates detail rows per employee per month.
        /// </summary>
        private async Task<List<AttendanceMonthlySummaryViewModel>> BuildMonthlySummaryAsync(
            AttendanceReportFilter filter)
        {
            var monthStart = filter.DateFrom?.Date ?? new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1);
            var monthEnd   = filter.DateTo?.Date   ?? DateTime.Today;
            int targetMonth = monthStart.Month;
            int targetYear  = monthStart.Year;

            // Reuse detail builder with the selected date range
            var detailFilter = new AttendanceReportFilter
            {
                DateFrom = monthStart,
                DateTo = monthEnd,
                EmployeeId = filter.EmployeeId,
                EmployeeName = filter.EmployeeName,
                Department = filter.Department,
                Location = filter.Location,
                ReportType = "Detail"
            };

            var detail = await BuildDetailReportAsync(detailFilter);

            int workingDaysInMonth = (monthEnd - monthStart).Days + 1;

            var summary = detail.GroupBy(r => new { r.EmployeeId, r.EmployeeName, r.Department, r.Location })
                .Select(g =>
                {
                    double totalWorked = g.Where(r => r.TotalWorkingHours.HasValue).Sum(r => r.TotalWorkingHours!.Value);
                    double totalOvertime = g.Where(r => r.OvertimeHours.HasValue).Sum(r => r.OvertimeHours!.Value);
                    int presentDays = g.Count(r => !r.IsAbsent);
                    int lateDays = g.Count(r => r.IsLate);

                    return new AttendanceMonthlySummaryViewModel
                    {
                        EmployeeId = g.Key.EmployeeId,
                        EmployeeName = g.Key.EmployeeName,
                        Department = g.Key.Department,
                        Location = g.Key.Location,
                        Month = targetMonth,
                        Year = targetYear,
                        TotalDays = workingDaysInMonth,
                        PresentDays = presentDays,
                        AbsentDays = Math.Max(0, workingDaysInMonth - presentDays),
                        LateDays = lateDays,
                        TotalWorkedHours = Math.Round(totalWorked, 2),
                        TotalWorkedLabel = FormatHours(TimeSpan.FromHours(totalWorked)),
                        TotalOvertimeHours = Math.Round(totalOvertime, 2),
                        TotalOvertimeLabel = totalOvertime > 0 ? FormatHours(TimeSpan.FromHours(totalOvertime)) : "—"
                    };
                }).OrderBy(s => s.EmployeeName).ToList();

            return summary;
        }

        /// <summary>
        /// Absentee report: employees who have NO punch records on a given date range.
        /// Compares all employees who punched at least once during the broader period
        /// against each date in the range.
        /// </summary>
        private async Task<List<AbsenteeReportViewModel>> BuildAbsenteeReportAsync(AttendanceReportFilter filter)
        {
            var dateFrom = filter.DateFrom ?? DateTime.Today.AddDays(-30);
            var dateTo = filter.DateTo ?? DateTime.Today;

            // All dates in range 
            var allDates = Enumerable.Range(0, (dateTo.Date - dateFrom.Date).Days + 1).Select(d => dateFrom.Date.AddDays(d)).ToList();

            // All employees who have any record in the system (within filter scope)
            var empQuery = _db.HR_Swap_Record.Join(_db.AttendenceMachines, s => s.MachineId, m => m.Id,
                    (s, m) => new
                    {
                        s.Emp_No,
                        EmpName = s.Emp_Name ?? "",
                        RecordDate = s.Swap_Time.HasValue ? s.Swap_Time.Value.Date : DateTime.MinValue,
                        Location = m.Location,
                        Department = (string)null
                    });

            if (!string.IsNullOrWhiteSpace(filter.EmployeeId))
                empQuery = empQuery.Where(x => x.Emp_No.Contains(filter.EmployeeId));

            if (!string.IsNullOrWhiteSpace(filter.Location))
                empQuery = empQuery.Where(x => x.Location.Contains(filter.Location));

            var allRecords = await empQuery.Where(x => x.RecordDate >= dateFrom.Date && x.RecordDate <= dateTo.Date).Select(x => new { x.Emp_No, x.EmpName, x.RecordDate, x.Location, x.Department }).Distinct().ToListAsync();

            // Distinct employees
            var employees = allRecords.Select(r => new { r.Emp_No, r.EmpName, r.Location, r.Department }).Distinct().ToList();

            // Dates each employee DID punch
            var presentSet = allRecords.Select(r => (r.Emp_No, r.RecordDate)).ToHashSet();

            var absent = new List<AbsenteeReportViewModel>();

            foreach (var emp in employees)
            {
                foreach (var date in allDates)
                {
                    if (!presentSet.Contains((emp.Emp_No, date)))
                    {
                        absent.Add(new AbsenteeReportViewModel
                        {
                            EmployeeId = emp.Emp_No,
                            EmployeeName = emp.EmpName,
                            Department = emp.Department ?? "N/A",
                            Location = emp.Location ?? "N/A",
                            Date = date
                        });
                    }
                }
            }
            return absent.OrderBy(a => a.Date).ThenBy(a => a.EmployeeId).ToList();
        }

        // ===============================================================
        //  EXCEL BUILDERS
        // ===============================================================

        private static void BuildDetailExcelSheet(XLWorkbook wb,List<AttendanceReportViewModel> data)
        {
            var ws = wb.Worksheets.Add("Attendance Detail");

            string[] headers = { "Employee ID", "Employee Name", "Department", "Location","Date", "Check-In", "Check-Out","Total Working Hours", "Late Mark", "Overtime"};

            WriteExcelHeader(ws, headers);

            int row = 2;
            foreach (var r in data)
            {
                ws.Cell(row, 1).Value = r.EmployeeId;
                ws.Cell(row, 2).Value = r.EmployeeName;
                ws.Cell(row, 3).Value = r.Department;
                ws.Cell(row, 4).Value = r.Location;
                ws.Cell(row, 5).Value = r.DateLabel;
                ws.Cell(row, 6).Value = r.CheckInLabel;
                ws.Cell(row, 7).Value = r.CheckOutLabel;
                ws.Cell(row, 8).Value = r.TotalHoursLabel;

                var lateCell = ws.Cell(row, 9);
                lateCell.Value = r.IsLate ? "Yes" : "No";
                if (r.IsLate)
                {
                    lateCell.Style.Font.FontColor = XLColor.FromHtml("#dc3545");
                    lateCell.Style.Font.Bold = true;
                }
                ws.Cell(row, 10).Value = r.OvertimeLabel;
                row++;
            }

            ws.Columns().AdjustToContents();
        }

        private static void BuildMonthlyExcelSheet(XLWorkbook wb,List<AttendanceMonthlySummaryViewModel> data)
        {
            var ws = wb.Worksheets.Add("Monthly Summary");

            string[] headers = {"Employee ID", "Employee Name", "Department", "Location","Month", "Total Days", "Present", "Absent", "Late Days","Total Worked Hours", "Total Overtime"};

            WriteExcelHeader(ws, headers);

            int row = 2;
            foreach (var r in data)
            {
                ws.Cell(row, 1).Value = r.EmployeeId;
                ws.Cell(row, 2).Value = r.EmployeeName;
                ws.Cell(row, 3).Value = r.Department;
                ws.Cell(row, 4).Value = r.Location;
                ws.Cell(row, 5).Value = r.MonthLabel;
                ws.Cell(row, 6).Value = r.TotalDays;
                ws.Cell(row, 7).Value = r.PresentDays;

                var absentCell = ws.Cell(row, 8);
                absentCell.Value = r.AbsentDays;
                if (r.AbsentDays > 0)
                    absentCell.Style.Font.FontColor = XLColor.FromHtml("#dc3545");

                var lateCell = ws.Cell(row, 9);
                lateCell.Value = r.LateDays;
                if (r.LateDays > 0)
                    lateCell.Style.Font.FontColor = XLColor.FromHtml("#fd7e14");

                ws.Cell(row, 10).Value = r.TotalWorkedLabel;
                ws.Cell(row, 11).Value = r.TotalOvertimeLabel;
                row++;
            }

            ws.Columns().AdjustToContents();
        }

        private static void BuildAbsenteeExcelSheet(XLWorkbook wb,List<AbsenteeReportViewModel> data)
        {
            var ws = wb.Worksheets.Add("Absentee Report");

            string[] headers = {"Employee ID", "Employee Name", "Department", "Location", "Date", "Day"};

            WriteExcelHeader(ws, headers);

            int row = 2;
            foreach (var r in data)
            {
                ws.Cell(row, 1).Value = r.EmployeeId;
                ws.Cell(row, 2).Value = r.EmployeeName;
                ws.Cell(row, 3).Value = r.Department;
                ws.Cell(row, 4).Value = r.Location;
                ws.Cell(row, 5).Value = r.DateLabel;
                ws.Cell(row, 6).Value = r.DayName;
                row++;
            }

            ws.Columns().AdjustToContents();
        }

        private static void WriteExcelHeader(IXLWorksheet ws, string[] headers)
        {
            for (int c = 0; c < headers.Length; c++)
            {
                var cell = ws.Cell(1, c + 1);
                cell.Value = headers[c];
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#1e3a5f");
                cell.Style.Font.FontColor = XLColor.White;
                cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            }
        }

        // ===============================================================
        //  PDF BUILDERS
        // ===============================================================

        private static void BuildDetailPdf(Document doc,List<AttendanceReportViewModel> data,AttendanceReportFilter filter)
        {
            AddPdfTitle(doc, "Attendance Detail Report", filter);

            var table = new PdfPTable(10) { WidthPercentage = 100 };
            table.SetWidths(new float[] { 9, 14, 10, 10, 9, 8, 8, 10, 7, 8 });

            string[] headers = {"Emp ID", "Name", "Department", "Location","Date", "Check-In", "Check-Out","Worked Hrs", "Late", "Overtime"
            };

            AddPdfHeaders(table, headers);

            var cellFont = FontFactory.GetFont(FontFactory.HELVETICA, 7, BaseColor.BLACK);
            var lateFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 7, new BaseColor(220, 53, 69));
            var otFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 7, new BaseColor(13, 110, 253));

            foreach (var r in data)
            {
                table.AddCell(new PdfPCell(new Phrase(r.EmployeeId, cellFont)) { Padding = 3 });
                table.AddCell(new PdfPCell(new Phrase(r.EmployeeName, cellFont)) { Padding = 3 });
                table.AddCell(new PdfPCell(new Phrase(r.Department, cellFont)) { Padding = 3 });
                table.AddCell(new PdfPCell(new Phrase(r.Location, cellFont)) { Padding = 3 });
                table.AddCell(new PdfPCell(new Phrase(r.DateLabel, cellFont)) { Padding = 3 });
                table.AddCell(new PdfPCell(new Phrase(r.CheckInLabel, cellFont)) { Padding = 3 });
                table.AddCell(new PdfPCell(new Phrase(r.CheckOutLabel, cellFont)) { Padding = 3 });
                table.AddCell(new PdfPCell(new Phrase(r.TotalHoursLabel, cellFont)) { Padding = 3 });

                var lateCell = new PdfPCell(new Phrase(r.IsLate ? "Yes" : "No", r.IsLate ? lateFont : cellFont))
                { Padding = 3, HorizontalAlignment = Element.ALIGN_CENTER };
                table.AddCell(lateCell);

                table.AddCell(new PdfPCell(new Phrase(r.OvertimeLabel, r.OvertimeHours > 0 ? otFont : cellFont))
                { Padding = 3, HorizontalAlignment = Element.ALIGN_CENTER });
            }

            doc.Add(table);
        }

        private static void BuildMonthlyPdf(Document doc,List<AttendanceMonthlySummaryViewModel> data,AttendanceReportFilter filter)
        {
            AddPdfTitle(doc, "Monthly Attendance Summary", filter);

            var table = new PdfPTable(11) { WidthPercentage = 100 };
            table.SetWidths(new float[] { 9, 14, 10, 10, 10, 7, 7, 7, 7, 10, 10 });

            string[] headers = {"Emp ID", "Name", "Department", "Location", "Month","Days", "Present", "Absent", "Late","Worked Hrs", "Overtime"};

            AddPdfHeaders(table, headers);

            var cellFont = FontFactory.GetFont(FontFactory.HELVETICA, 7, BaseColor.BLACK);
            var redFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 7, new BaseColor(220, 53, 69));
            var orgFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 7, new BaseColor(253, 126, 20));

            foreach (var r in data)
            {
                table.AddCell(new PdfPCell(new Phrase(r.EmployeeId, cellFont)) { Padding = 3 });
                table.AddCell(new PdfPCell(new Phrase(r.EmployeeName, cellFont)) { Padding = 3 });
                table.AddCell(new PdfPCell(new Phrase(r.Department, cellFont)) { Padding = 3 });
                table.AddCell(new PdfPCell(new Phrase(r.Location, cellFont)) { Padding = 3 });
                table.AddCell(new PdfPCell(new Phrase(r.MonthLabel, cellFont)) { Padding = 3 });
                table.AddCell(new PdfPCell(new Phrase(r.TotalDays.ToString(), cellFont)) { Padding = 3, HorizontalAlignment = Element.ALIGN_CENTER });
                table.AddCell(new PdfPCell(new Phrase(r.PresentDays.ToString(), cellFont)) { Padding = 3, HorizontalAlignment = Element.ALIGN_CENTER });
                table.AddCell(new PdfPCell(new Phrase(r.AbsentDays.ToString(), r.AbsentDays > 0 ? redFont : cellFont)) { Padding = 3, HorizontalAlignment = Element.ALIGN_CENTER });
                table.AddCell(new PdfPCell(new Phrase(r.LateDays.ToString(), r.LateDays > 0 ? orgFont : cellFont)) { Padding = 3, HorizontalAlignment = Element.ALIGN_CENTER });
                table.AddCell(new PdfPCell(new Phrase(r.TotalWorkedLabel, cellFont)) { Padding = 3, HorizontalAlignment = Element.ALIGN_CENTER });
                table.AddCell(new PdfPCell(new Phrase(r.TotalOvertimeLabel, cellFont)) { Padding = 3, HorizontalAlignment = Element.ALIGN_CENTER });
            }

            doc.Add(table);
        }

        private static void BuildAbsenteePdf(Document doc,List<AbsenteeReportViewModel> data,AttendanceReportFilter filter)
        {
            AddPdfTitle(doc, "Absentee Report", filter);

            var table = new PdfPTable(6) { WidthPercentage = 100 };
            table.SetWidths(new float[] { 12, 20, 15, 15, 15, 12 });

            string[] headers = { "Emp ID", "Name", "Department", "Location", "Date", "Day" };
            AddPdfHeaders(table, headers);

            var cellFont = FontFactory.GetFont(FontFactory.HELVETICA, 8, BaseColor.BLACK);

            foreach (var r in data)
            {
                table.AddCell(new PdfPCell(new Phrase(r.EmployeeId, cellFont)) { Padding = 4 });
                table.AddCell(new PdfPCell(new Phrase(r.EmployeeName, cellFont)) { Padding = 4 });
                table.AddCell(new PdfPCell(new Phrase(r.Department, cellFont)) { Padding = 4 });
                table.AddCell(new PdfPCell(new Phrase(r.Location, cellFont)) { Padding = 4 });
                table.AddCell(new PdfPCell(new Phrase(r.DateLabel, cellFont)) { Padding = 4 });
                table.AddCell(new PdfPCell(new Phrase(r.DayName, cellFont)) { Padding = 4 });
            }

            doc.Add(table);
        }

        private static void AddPdfTitle(Document doc, string title,AttendanceReportFilter filter)
        {
            var titleFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 13, new BaseColor(30, 58, 95));
            var subFont = FontFactory.GetFont(FontFactory.HELVETICA, 9, BaseColor.GRAY);

            doc.Add(new Paragraph(title, titleFont));
            doc.Add(new Paragraph(
                $"Generated: {DateTime.Now:dd-MMM-yyyy hh:mm tt}   " +
                $"Period: {filter.DateFrom?.ToString("dd-MMM-yyyy") ?? "All"} — " +
                $"{filter.DateTo?.ToString("dd-MMM-yyyy") ?? "All"}", subFont));
            doc.Add(new Paragraph(" "));
        }

        private static void AddPdfHeaders(PdfPTable table, string[] headers)
        {
            var hdrBg = new BaseColor(30, 58, 95);
            var hdrFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 8, BaseColor.WHITE);

            foreach (var h in headers)
            {
                table.AddCell(new PdfPCell(new Phrase(h, hdrFont))
                {
                    BackgroundColor = hdrBg,
                    Padding = 5,
                    HorizontalAlignment = Element.ALIGN_CENTER
                });
            }
        }

        // ===============================================================
        //  HELPER UTILITIES
        // ===============================================================

        private static string FormatHours(TimeSpan span)
        {
            int h = (int)Math.Floor(Math.Abs(span.TotalHours));
            int m = Math.Abs(span.Minutes);
            return $"{h:D2}h {m:D2}m";
        }

        private AttendanceReportFilter ExtractFilter(
            Microsoft.AspNetCore.Http.IFormCollection form)
        {
            DateTime.TryParseExact(form["dateFrom"].FirstOrDefault(),new[] { "dd-MMM-yyyy", "yyyy-MM-dd", "MM/dd/yyyy" },System.Globalization.CultureInfo.InvariantCulture,System.Globalization.DateTimeStyles.None, out var df);

            DateTime.TryParseExact(form["dateTo"].FirstOrDefault(),new[] { "dd-MMM-yyyy", "yyyy-MM-dd", "MM/dd/yyyy" },System.Globalization.CultureInfo.InvariantCulture,System.Globalization.DateTimeStyles.None, out var dt);
            int.TryParse(form["month"].FirstOrDefault(), out var mo);
            int.TryParse(form["year"].FirstOrDefault(), out var yr);

            return new AttendanceReportFilter
            {
                DateFrom = df == default ? null : df,
                DateTo = dt == default ? null : dt,
                EmployeeId = form["employeeId"].FirstOrDefault(),
                EmployeeName = form["employeeName"].FirstOrDefault(),
                Department = form["department"].FirstOrDefault(),
                Location = form["location"].FirstOrDefault(),
                Month = mo == 0 ? null : mo,
                Year = yr == 0 ? null : yr,
                ReportType = form["reportType"].FirstOrDefault() ?? "Detail"
            };
        }

        private AttendanceReportFilter BuildFilterFromQS(string reportType, string dateFrom, string dateTo,string employeeId, string employeeName, string department, string location,string month, string year)
        {
       
            DateTime.TryParseExact(dateFrom,new[] { "dd-MMM-yyyy", "yyyy-MM-dd", "MM/dd/yyyy" },System.Globalization.CultureInfo.InvariantCulture,System.Globalization.DateTimeStyles.None, out var df);
            DateTime.TryParseExact(dateTo,new[] { "dd-MMM-yyyy", "yyyy-MM-dd", "MM/dd/yyyy" },System.Globalization.CultureInfo.InvariantCulture,System.Globalization.DateTimeStyles.None, out var dt);
            int.TryParse(month, out var mo);
            int.TryParse(year, out var yr);

            return new AttendanceReportFilter
            {
                ReportType = reportType ?? "Detail",
                DateFrom = df == default ? null : df,
                DateTo = dt == default ? null : dt,
                EmployeeId = employeeId,
                EmployeeName = employeeName,
                Department = department,
                Location = location,
                Month = mo == 0 ? (int?)null : mo,
                Year = yr == 0 ? (int?)null : yr
            };
        }

        private async Task<List<string>> GetDepartmentsAsync()
        {
            // If you have a Department table/column, query it here.
            // Returning empty for now — populate when department data is available.
            return await Task.FromResult(new List<string>());
        }

        private async Task<List<LocationDropdown>> GetLocationsAsync()
        {
            return await _db.AttendenceMachines.Where(m => m.IsActive)
                .Select(m => new LocationDropdown
                {
                    Id = m.Id,
                    Name = m.Location  // adjust property name to match your entity
                })
                .Distinct()
                .OrderBy(l => l.Name)
                .ToListAsync();
        }
    }
}