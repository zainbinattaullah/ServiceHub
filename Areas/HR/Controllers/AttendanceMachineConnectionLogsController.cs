using ClosedXML.Excel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ServiceHub.Areas.HR.Models;
using ServiceHub.Data;
using System;
using System.Buffers;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace ServiceHub.Areas.HR.Controllers
{
    [Area("HR")]
    [Authorize]
    public class AttendanceMachineConnectionLogsController : Controller
    {
        private readonly ServiceHubContext _dbcontext;
        public AttendanceMachineConnectionLogsController(ServiceHubContext context)
        {
            _dbcontext = context;
        }
        public IActionResult Index()
        {
            return View();
        }
        [HttpPost]
        public async Task<IActionResult> GetConnectionLogs()
        {
            var request = HttpContext?.Request?.Form;
            var draw = request?["draw"].FirstOrDefault() ?? string.Empty;
            var start = request?["start"].FirstOrDefault() ?? string.Empty;
            var length = request?["length"].FirstOrDefault() ?? string.Empty;
            var searchValue = request?["search[value]"].FirstOrDefault() ?? string.Empty;
            var sortColumnIndex = request?["order[0][column]"].FirstOrDefault() ?? string.Empty;
            var sortColumnName = request?[$"columns[{sortColumnIndex}][data]"].FirstOrDefault() ?? string.Empty;
            var sortDirection = request?["order[0][dir]"].FirstOrDefault() ?? string.Empty;

            int pageSize = int.TryParse(length, out var l) ? l : 10;
            int skip = int.TryParse(start, out var s) ? s : 0;
            var query = from log in _dbcontext.AttendenceMachineConnectionLogs
                        join machine in _dbcontext.AttendenceMachines
                             on log.MachineId equals machine.Id
                             orderby log.Id descending
                        select new
                        {
                            log,
                            MachineName = machine.Name
                        };
            if (!string.IsNullOrEmpty(searchValue))
            {
                searchValue = searchValue.ToLower();

                query = query.Where(m =>
                    m.log.Id.ToString().Contains(searchValue) ||
                    ((m.MachineName ?? string.Empty).ToLower().Contains(searchValue)) ||
                    ((m.log.Machine_IP ?? string.Empty).ToLower().Contains(searchValue)) ||
                    m.log.Connection_StartTime.ToString("dd-MMM-yyyy hh:mm tt").ToLower().Contains(searchValue) ||
                    (m.log.Connection_EndTime.HasValue && m.log.Connection_EndTime.Value.ToString("dd-MMM-yyyy hh:mm tt").ToLower().Contains(searchValue)) ||
                    ((m.log.Status ?? string.Empty).ToLower().Contains(searchValue)) ||
                    (!string.IsNullOrEmpty(m.log.ErrorMessage) && m.log.ErrorMessage.ToLower().Contains(searchValue)) ||
                    (m.log.RecordsRead.HasValue && m.log.RecordsRead.Value.ToString().Contains(searchValue))
                );
            }
            // 🔁 Dynamic sort logic
            if (!string.IsNullOrEmpty(sortColumnName) && !string.IsNullOrEmpty(sortDirection))
            {
                var isAsc = sortDirection == "asc";
                query = sortColumnName switch
                {
                    "id" => isAsc ? query.OrderBy(x => x.log.Id) : query.OrderByDescending(x => x.log.Id),
                    "machineName" => isAsc ? query.OrderBy(x => x.MachineName) : query.OrderByDescending(x => x.MachineName),
                    "machine_IP" => isAsc ? query.OrderBy(x => x.log.Machine_IP) : query.OrderByDescending(x => x.log.Machine_IP),
                    "connection_StartTime" => isAsc ? query.OrderBy(x => x.log.Connection_StartTime) : query.OrderByDescending(x => x.log.Connection_StartTime),
                    "connection_EndTime" => isAsc ? query.OrderBy(x => x.log.Connection_EndTime) : query.OrderByDescending(x => x.log.Connection_EndTime),
                    "status" => isAsc ? query.OrderBy(x => x.log.Status) : query.OrderByDescending(x => x.log.Status),
                    "errorMessage" => isAsc ? query.OrderBy(x => x.log.ErrorMessage) : query.OrderByDescending(x => x.log.ErrorMessage),
                    "recordsRead" => isAsc ? query.OrderBy(x => x.log.RecordsRead) : query.OrderByDescending(x => x.log.RecordsRead),
                    _ => query.OrderByDescending(x => x.log.Id)
                };
            }
            var totalRecords = await query.CountAsync();
            var queryData = await query.Skip(skip).Take(pageSize).ToListAsync();
            DateTime currentTime = DateTime.Now;
            var data = queryData.Select(m => new
            {
                id = m.log.Id,
                machineName = m.MachineName,
                machine_IP = m.log.Machine_IP,
                connection_StartTime = m.log.Connection_StartTime.ToString("dd-MMM-yyyy hh:mm tt"),
                connection_EndTime = m.log.Connection_EndTime.HasValue ? m.log.Connection_EndTime.Value.ToString("dd-MMM-yyyy hh:mm tt") : "N/A",
                status = m.log.Status,
                errorMessage = m.log.ErrorMessage,
                recordsRead = m.log.RecordsRead,
                lastFetching = GetTimeAgo(m.log.Connection_StartTime, currentTime)
            }).ToList();
            return Json(new
            {
                draw = draw,
                recordsTotal = totalRecords,
                recordsFiltered = totalRecords,
                data = data
            });
        }
        private string GetTimeAgo(DateTime startTime, DateTime currentTime)
        {
            var timeSpan = currentTime - startTime;

            if (timeSpan.TotalMinutes < 1)
                return "Just now";
            if (timeSpan.TotalMinutes < 60)
                return $"{(int)timeSpan.TotalMinutes} minutes ago";
            if (timeSpan.TotalHours < 24)
                return $"{(int)timeSpan.TotalHours} hours ago";

            return $"{(int)timeSpan.TotalDays} days ago";
        }
        [HttpGet]
        public async Task<IActionResult> ExportConnectionLogs(string search = null, string sortColumn = null, string sortDirection = null)
        {
            var query = from log in _dbcontext.AttendenceMachineConnectionLogs
                        join machine in _dbcontext.AttendenceMachines
                             on log.MachineId equals machine.Id
                        orderby log.Id descending
                        select new
                        {
                            log,
                            MachineName = machine.Name
                        };
            // Apply search filter
            if (!string.IsNullOrEmpty(search))
            {
                search = search.ToLower();

                query = query.Where(m =>
                    m.log.Id.ToString().Contains(search) ||
                    ((m.MachineName ?? string.Empty).ToLower().Contains(search)) || 
                    ((m.log.Machine_IP ?? string.Empty).ToLower().Contains(search)) ||
                    m.log.Connection_StartTime.ToString("dd-MMM-yyyy hh:mm tt").ToLower().Contains(search) ||
                    (m.log.Connection_EndTime.HasValue && m.log.Connection_EndTime.Value.ToString("dd-MMM-yyyy hh:mm tt").ToLower().Contains(search)) ||
                    ((m.log.Status ?? string.Empty).ToLower().Contains(search)) ||
                    (!string.IsNullOrEmpty(m.log.ErrorMessage) && m.log.ErrorMessage.ToLower().Contains(search)) ||
                    (m.log.RecordsRead.HasValue && m.log.RecordsRead.Value.ToString().Contains(search))
                );
            }

            // Apply sorting
            if (!string.IsNullOrEmpty(sortColumn) && !string.IsNullOrEmpty(sortDirection))
            {
                var isAsc = sortDirection == "asc";
                query = sortColumn switch
                {
                    "id" => isAsc ? query.OrderBy(x => x.log.Id) : query.OrderByDescending(x => x.log.Id),
                    "machineName" => isAsc ? query.OrderBy(x => x.MachineName) : query.OrderByDescending(x => x.MachineName), 
                    "machine_IP" => isAsc ? query.OrderBy(x => x.log.Machine_IP) : query.OrderByDescending(x => x.log.Machine_IP),
                    "connection_StartTime" => isAsc ? query.OrderBy(x => x.log.Connection_StartTime) : query.OrderByDescending(x => x.log.Connection_StartTime),
                    "connection_EndTime" => isAsc ? query.OrderBy(x => x.log.Connection_EndTime) : query.OrderByDescending(x => x.log.Connection_EndTime),
                    "status" => isAsc ? query.OrderBy(x => x.log.Status) : query.OrderByDescending(x => x.log.Status),
                    "errorMessage" => isAsc ? query.OrderBy(x => x.log.ErrorMessage) : query.OrderByDescending(x => x.log.ErrorMessage),
                    "recordsRead" => isAsc ? query.OrderBy(x => x.log.RecordsRead) : query.OrderByDescending(x => x.log.RecordsRead),
                    _ => query.OrderByDescending(x => x.log.Id)
                };
            }

            // Fetch all filtered & sorted records
            var records = await query.Select(m => new
            {
                Id = m.log.Id,
                MachineName = m.MachineName, 
                Machine_IP = m.log.Machine_IP,
                Connection_StartTime = m.log.Connection_StartTime.ToString("dd-MMM-yyyy hh:mm tt"),
                Connection_EndTime = m.log.Connection_EndTime.HasValue
                         ? m.log.Connection_EndTime.Value.ToString("dd-MMM-yyyy hh:mm tt")
                         : "N/A",
                Status = m.log.Status,
                ErrorMessage = m.log.ErrorMessage ?? "N/A",
                RecordsRead = m.log.RecordsRead,
                LastFetching = GetConnectionTimeAgo(m.log.Connection_StartTime, DateTime.Now)
            }).ToListAsync();
            // Generate Excel file
            using (var workbook = new XLWorkbook())
            {
                IXLWorksheet worksheet = workbook.Worksheets.Add("Connection Logs");

                // Headers
                worksheet.Cell(1, 1).Value = "Machine Name";
                worksheet.Cell(1, 2).Value = "Machine IP";
                worksheet.Cell(1, 3).Value = "Connection Start Time";
                worksheet.Cell(1, 4).Value = "Connection End Time";
                worksheet.Cell(1, 5).Value = "Status";
                worksheet.Cell(1, 6).Value = "Error Message";
                worksheet.Cell(1, 7).Value = "Records Read";
                worksheet.Cell(1, 8).Value = "Last Fetching";
                int row = 2;
                foreach (var record in records)
                {
                    worksheet.Cell(row, 1).Value = record.MachineName;
                    worksheet.Cell(row, 2).Value = record.Machine_IP;
                    worksheet.Cell(row, 3).Value = record.Connection_StartTime;
                    worksheet.Cell(row, 4).Value = record.Connection_EndTime;
                    worksheet.Cell(row, 5).Value = record.Status;
                    worksheet.Cell(row, 6).Value = record.ErrorMessage;
                    worksheet.Cell(row, 7).Value = record.RecordsRead;
                    worksheet.Cell(row, 8).Value = record.LastFetching;
                    row++;
                }
                using (var stream = new MemoryStream())
                {
                    workbook.SaveAs(stream);
                    var content = stream.ToArray();
                    return File(content, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "MachineConnectionLogs.xlsx");
                }
            }
        }
        private static string GetConnectionTimeAgo(DateTime startTime, DateTime currentTime)
        {
            var timeSpan = currentTime - startTime;
            if (timeSpan.TotalMinutes < 1) return "Just now";
            if (timeSpan.TotalHours < 24) return $"{(int)timeSpan.TotalHours} hours ago";
            return $"{(int)timeSpan.TotalDays} days ago";
        }
    }
}

