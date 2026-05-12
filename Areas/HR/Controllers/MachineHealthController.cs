using ClosedXML.Excel;
using DocumentFormat.OpenXml.Office2010.PowerPoint;
using DocumentFormat.OpenXml.Wordprocessing;
using iTextSharp.text;
using iTextSharp.text.pdf;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ServiceHub.Areas.HR.Models;
using ServiceHub.Data;
using System.Xml.Linq;
using Paragraph = iTextSharp.text.Paragraph;

namespace ServiceHub.Areas.HR.Controllers
{
    [Area("HR")]
    [Authorize]
    public class MachineHealthController : Controller
    {
        private readonly ServiceHubContext _db;
        // A machine is considered Online if it had a SUCCESSFUL sync
        // within the last N minutes.
        private const int OnlineThresholdMinutes = 70;
        public MachineHealthController(ServiceHubContext db)
        {
            _db = db;
        }
        // ---------------------------------------------------------------
        //  GET /HR/MachineHealth/Index
        //  Returns the Razor shell page — the grid is loaded via AJAX.
        // ---------------------------------------------------------------
        public IActionResult Index()
        {
            return View();
        }
        // ---------------------------------------------------------------
        //  POST /HR/MachineHealth/GetHealthData
        //  DataTables server-side endpoint — returns JSON.
        // ---------------------------------------------------------------
        [HttpPost]
        public async Task<IActionResult> GetHealthData()
        {
            var form = HttpContext.Request.Form;
            var draw = form["draw"].FirstOrDefault() ?? "1";
            var start = int.Parse(form["start"].FirstOrDefault() ?? "0");
            var length = int.Parse(form["length"].FirstOrDefault() ?? "10");
            var searchValue = (form["search[value]"].FirstOrDefault() ?? "").ToLower().Trim();
            var statusFilter = (form["statusFilter"].FirstOrDefault() ?? "").Trim();
            var locationFilter = (form["locationFilter"].FirstOrDefault() ?? "").Trim();
            var machineFilter = (form["machineFilter"].FirstOrDefault() ?? "").Trim();
            var rows = await BuildHealthRowsAsync();
            // Apply filters
            if (!string.IsNullOrEmpty(statusFilter))
                rows = rows.Where(r => r.Status == statusFilter).ToList();

            if (!string.IsNullOrEmpty(locationFilter))
                rows = rows.Where(r =>r.LocationId.ToString() == locationFilter || (r.LocationName ?? "").ToLower().Contains(locationFilter.ToLower())).ToList();

            if (!string.IsNullOrEmpty(machineFilter))
                rows = rows.Where(r => r.MachineId.ToString() == machineFilter || (r.MachineName ?? "").ToLower().Contains(machineFilter.ToLower())).ToList();

            if (!string.IsNullOrEmpty(searchValue))
                rows = rows.Where(r =>(r.MachineName ?? "").ToLower().Contains(searchValue) ||(r.MachineIP ?? "").ToLower().Contains(searchValue) || (r.LocationName ?? "").ToLower().Contains(searchValue) ||(r.Status ?? "").ToLower().Contains(searchValue)).ToList();

            int total = rows.Count;
            var pageData = rows.Skip(start).Take(length).ToList();

            // Summary for header cards (always over unfiltered full set)
            var allRows = await BuildHealthRowsAsync();
            var summary = new MachineHealthSummary
            {
                TotalMachines = allRows.Count,
                OnlineMachines = allRows.Count(r => r.IsOnline),
                OfflineMachines = allRows.Count(r => !r.IsOnline)
            };

            return Json(new
            {
                draw = draw,
                recordsTotal = total,
                recordsFiltered = total,
                summary,
                data = pageData.Select(r => new
                {
                    r.MachineId,
                    r.MachineName,
                    r.MachineIP,
                    r.Port,
                    r.LocationName,
                    r.Status,
                    r.IsOnline,
                    lastCommunication = r.LastCommunication.HasValue ? r.LastCommunication.Value.ToString("dd-MMM-yyyy hh:mm tt") : "Never",
                    r.LastCommunicationLabel,
                    r.LastRecordsRead,
                    r.LastErrorMessage
                })
            });
        }

        // ---------------------------------------------------------------
        //  GET /HR/MachineHealth/GetSummary
        //  Lightweight endpoint polled every 60 s to refresh header cards.
        // ---------------------------------------------------------------
        [HttpGet]
        public async Task<IActionResult> GetSummary()
        {
            var rows = await BuildHealthRowsAsync();
            return Json(new
            {
                total = rows.Count,
                online = rows.Count(r => r.IsOnline),
                offline = rows.Count(r => !r.IsOnline)
            });
        }

        // ---------------------------------------------------------------
        //  GET /HR/MachineHealth/ExportExcel
        // ---------------------------------------------------------------
        [HttpGet]
        public async Task<IActionResult> ExportExcel(string status = null, string location = null, string machine = null)
        {
            var rows = await BuildHealthRowsAsync();
            rows = ApplyExportFilters(rows, status, location, machine);

            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add("Machine Health");

            // Header row
            string[] headers = {
                "Machine Name", "Machine IP", "Port", "Location / Branch",
                "Status", "Last Communication", "Last Fetched", "Records Read", "Last Error"
            };
            for (int c = 0; c < headers.Length; c++)
            {
                var cell = ws.Cell(1, c + 1);
                cell.Value = headers[c];
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#1e3a5f");
                cell.Style.Font.FontColor = XLColor.White;
            }

            // Data rows
            int row = 2;
            foreach (var r in rows)
            {
                ws.Cell(row, 1).Value = r.MachineName;
                ws.Cell(row, 2).Value = r.MachineIP;
                ws.Cell(row, 3).Value = r.Port;
                ws.Cell(row, 4).Value = r.LocationName ?? "N/A";
                ws.Cell(row, 5).Value = r.Status;
                ws.Cell(row, 6).Value = r.LastCommunication.HasValue ? r.LastCommunication.Value.ToString("dd-MMM-yyyy hh:mm tt") : "Never";
                ws.Cell(row, 7).Value = r.LastCommunicationLabel;
                ws.Cell(row, 8).Value = r.LastRecordsRead ?? 0;
                ws.Cell(row, 9).Value = r.LastErrorMessage ?? "N/A";
                // Color Online green, Offline red
                var statusCell = ws.Cell(row, 5);
                statusCell.Style.Font.FontColor = r.IsOnline? XLColor.FromHtml("#198754") : XLColor.FromHtml("#dc3545");
                statusCell.Style.Font.Bold = true;
                row++;
            }
            ws.Columns().AdjustToContents();
            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            return File(ms.ToArray(),"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",$"MachineHealth_{DateTime.Now:yyyyMMdd_HHmm}.xlsx");
        }

        // ---------------------------------------------------------------
        //  GET /HR/MachineHealth/ExportPdf
        // ---------------------------------------------------------------
        [HttpGet]
        public async Task<IActionResult> ExportPdf(string status = null, string location = null, string machine = null)
        {
            var rows = await BuildHealthRowsAsync();
            rows = ApplyExportFilters(rows, status, location, machine);

            using var ms = new MemoryStream();
            var doc = new iTextSharp.text.Document(iTextSharp.text.PageSize.A4.Rotate(), 20f, 20f, 30f, 30f);
            PdfWriter.GetInstance(doc, ms);
            doc.Open();

            // Title
            var titleFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 14,
                new BaseColor(30, 58, 95));
            doc.Add(new Paragraph($"Machine Health Status Report", titleFont));
            doc.Add(new Paragraph($"Generated: {DateTime.Now:dd-MMM-yyyy hh:mm tt}",
                FontFactory.GetFont(FontFactory.HELVETICA, 9, BaseColor.GRAY)));
            doc.Add(new Paragraph(" "));

            // Table
            var table = new PdfPTable(8) { WidthPercentage = 100 };
            table.SetWidths(new float[] { 16, 12, 6, 14, 8, 16, 12, 16 });

            string[] headers = {"Machine Name", "Machine IP", "Port", "Location","Status", "Last Communication", "Records Read", "Last Error"};

            var hdrBg = new BaseColor(30, 58, 95);
            var hdrFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 8, BaseColor.WHITE);
            var cellFont = FontFactory.GetFont(FontFactory.HELVETICA, 8, BaseColor.BLACK);
            var onlineC = new BaseColor(25, 135, 84);
            var offlineC = new BaseColor(220, 53, 69);

            foreach (var h in headers)
            {
                var cell = new PdfPCell(new Phrase(h, hdrFont))
                {
                    BackgroundColor = hdrBg,
                    Padding = 5,
                    HorizontalAlignment = Element.ALIGN_CENTER
                };
                table.AddCell(cell);
            }

            foreach (var r in rows)
            {
                table.AddCell(new PdfPCell(new Phrase(r.MachineName ?? "", cellFont)) { Padding = 4 });
                table.AddCell(new PdfPCell(new Phrase(r.MachineIP ?? "", cellFont)) { Padding = 4 });
                table.AddCell(new PdfPCell(new Phrase(r.Port.ToString(), cellFont)) { Padding = 4 });
                table.AddCell(new PdfPCell(new Phrase(r.LocationName ?? "N/A", cellFont)) { Padding = 4 });

                var statusCell = new PdfPCell(new Phrase(r.Status, FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 8, r.IsOnline ? onlineC : offlineC)))
                {
                    Padding = 4,
                    HorizontalAlignment = Element.ALIGN_CENTER
                };
                table.AddCell(statusCell);

                table.AddCell(new PdfPCell(new Phrase(r.LastCommunication.HasValue? r.LastCommunication.Value.ToString("dd-MMM-yyyy hh:mm tt") : "Never", cellFont))
                { Padding = 4 });

                table.AddCell(new PdfPCell(new Phrase((r.LastRecordsRead ?? 0).ToString(), cellFont))
                { Padding = 4, HorizontalAlignment = Element.ALIGN_CENTER });

                table.AddCell(new PdfPCell(new Phrase(r.LastErrorMessage ?? "N/A", cellFont))
                { Padding = 4 });
            }

            doc.Add(table);
            doc.Close();

            return File(ms.ToArray(), "application/pdf",
                $"MachineHealth_{DateTime.Now:yyyyMMdd_HHmm}.pdf");
        }

        // ===============================================================
        //  PRIVATE HELPERS
        // ===============================================================

        /// <summary>
        /// Core query: joins AttendenceMachines with their latest log entry
        /// and computes Online / Offline status.
        /// </summary>
        private async Task<List<MachineHealthViewModel>> BuildHealthRowsAsync()
        {
            DateTime threshold = DateTime.Now.AddMinutes(-OnlineThresholdMinutes);

            // Latest log per machine — two-step query (EF Core cannot translate
            // GroupBy+OrderBy+FirstOrDefault to SQL; Max(Id) is always translatable)
            var latestLogIds = await _db.AttendenceMachineConnectionLogs
                .GroupBy(l => l.MachineId)
                .Select(g => g.Max(l => l.Id))
                .ToListAsync();

            var latestLogs = await _db.AttendenceMachineConnectionLogs
                .Where(l => latestLogIds.Contains(l.Id))
                .ToListAsync();

            var logDict = latestLogs.ToDictionary(l => l.MachineId);

            var machines = await _db.AttendenceMachines.Where(m => m.IsActive).ToListAsync();

            var rows = new List<MachineHealthViewModel>();

            foreach (var m in machines)
            {
                logDict.TryGetValue(m.Id, out var log);

                // A machine is Online only if its last SUCCESSFUL sync is recent
                bool online = log != null && log.Status == "Success" && log.Connection_StartTime >= threshold;

                DateTime? lastComm = log?.Connection_StartTime;
                string label = lastComm.HasValue? GetTimeAgoLabel(lastComm.Value, DateTime.Now): "Never connected";

                rows.Add(new MachineHealthViewModel
                {
                    MachineId = m.Id,
                    MachineName = m.Name,
                    MachineIP = m.IpAddress,
                    Port = m.Port,
                    LocationName = m.Location,   // adjust property name to match your entity                    
                    Status = online ? "Online" : "Offline",
                    LastCommunication = lastComm,
                    LastCommunicationLabel = label,
                    LastRecordsRead = log?.RecordsRead,
                    LastErrorMessage = log?.Status != "Success" ? log?.ErrorMessage : null
                });
            }

            return rows.OrderBy(r => r.IsOnline ? 0 : 1).ThenBy(r => r.MachineName).ToList();
        }

        private static List<MachineHealthViewModel> ApplyExportFilters(List<MachineHealthViewModel> rows,string status, string location, string machine)
        {
            if (!string.IsNullOrEmpty(status))
                rows = rows.Where(r => r.Status == status).ToList();
            if (!string.IsNullOrEmpty(location))
                rows = rows.Where(r =>
                    (r.LocationName ?? "").ToLower().Contains(location.ToLower())).ToList();
            if (!string.IsNullOrEmpty(machine))
                rows = rows.Where(r =>
                    (r.MachineName ?? "").ToLower().Contains(machine.ToLower()) ||
                    r.MachineId.ToString() == machine).ToList();
            return rows;
        }

        private static string GetTimeAgoLabel(DateTime past, DateTime now)
        {
            var span = now - past;
            if (span.TotalMinutes < 1) return "Just now";
            if (span.TotalMinutes < 60) return $"{(int)span.TotalMinutes} min ago";
            if (span.TotalHours < 24) return $"{(int)span.TotalHours} hr ago";
            return $"{(int)span.TotalDays} day(s) ago";
        }
    }
}
