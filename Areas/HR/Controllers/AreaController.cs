using ClosedXML.Excel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ServiceHub.Data;
using ServiceHub.Areas.HR.Models;

namespace ServiceHub.Areas.HR.Controllers
{
    [Area("HR")]
    [Authorize]
    public class AreaController : Controller
    {
        private readonly ServiceHubContext _db;
        private readonly ILogger<AreaController> _logger;
        public AreaController(ServiceHubContext db, ILogger<AreaController> logger)
        {
            _db = db;
            _logger = logger;
        }
        public IActionResult Index() => View();
        [HttpPost]
        public async Task<IActionResult> GetAreas()
        {
            var form = HttpContext.Request.Form;
            var draw = form["draw"].FirstOrDefault();
            var start = Convert.ToInt32(form["start"].FirstOrDefault() ?? "0");
            var length = Convert.ToInt32(form["length"].FirstOrDefault() ?? "10");
            var searchValue = form["search[value]"].FirstOrDefault();
            var sortColIndex = form["order[0][column]"].FirstOrDefault();
            var sortColName = form["columns[" + sortColIndex + "][name]"].FirstOrDefault();
            var sortDirection = form["order[0][dir]"].FirstOrDefault();
            var query = _db.Areas.AsQueryable();
            if (!string.IsNullOrEmpty(searchValue))
            {
                var s = searchValue.ToLower();
                query = query.Where(a =>
                    a.Code.Contains(searchValue) ||
                    a.Name.Contains(searchValue) ||
                    (a.Description ?? "").Contains(searchValue) ||
                    (s == "yes" && a.IsActive) ||
                    (s == "no" && !a.IsActive));
            }
            query = sortColName switch
            {
                "code"        => sortDirection == "asc" ? query.OrderBy(a => a.Code)        : query.OrderByDescending(a => a.Code),
                "name"        => sortDirection == "asc" ? query.OrderBy(a => a.Name)        : query.OrderByDescending(a => a.Name),
                "description" => sortDirection == "asc" ? query.OrderBy(a => a.Description) : query.OrderByDescending(a => a.Description),
                "isActive"    => sortDirection == "asc" ? query.OrderBy(a => a.IsActive)    : query.OrderByDescending(a => a.IsActive),
                _             => query.OrderBy(a => a.Name)
            };
            int total = await query.CountAsync();
            var data = await query.Skip(start).Take(length)
                .Select(a => new
                {
                    a.Id,
                    a.Code,
                    a.Name,
                    a.Description,
                    a.IsActive,
                    createdAt = a.CreatedAt.ToString("dd MMM yyyy")
                })
                .ToListAsync();
            return Json(new { draw, recordsTotal = total, recordsFiltered = total, data });
        }
        public IActionResult Create() => View(new Area());

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(Area model)
        {
            if (!ModelState.IsValid) return View(model);
            if (await _db.Areas.AnyAsync(a => a.Code.ToLower() == model.Code.ToLower()))
            {
                ModelState.AddModelError("Code", "An area with this code already exists.");
                return View(model);
            }

            model.CreatedAt = DateTime.Now;
            _db.Areas.Add(model);
            await _db.SaveChangesAsync();
            TempData["Success"] = $"Area '{model.Name}' created successfully.";
            return RedirectToAction(nameof(Index));
        }

        public async Task<IActionResult> Edit(int id)
        {
            var area = await _db.Areas.FindAsync(id);
            if (area == null) return NotFound();
            return View(area);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, Area model)
        {
            if (id != model.Id) return NotFound();
            if (!ModelState.IsValid) return View(model);

            if (await _db.Areas.AnyAsync(a => a.Code.ToLower() == model.Code.ToLower() && a.Id != model.Id))
            {
                ModelState.AddModelError("Code", "Another area with this code already exists.");
                return View(model);
            }

            model.UpdatedAt = DateTime.Now;
            _db.Update(model);

            try { await _db.SaveChangesAsync(); }
            catch (DbUpdateConcurrencyException)
            {
                if (!await _db.Areas.AnyAsync(a => a.Id == id)) return NotFound();
                throw;
            }

            TempData["Success"] = $"Area '{model.Name}' updated successfully.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        public async Task<IActionResult> ToggleActive(int id)
        {
            var area = await _db.Areas.FindAsync(id);
            if (area == null) return NotFound(new { success = false });

            area.IsActive = !area.IsActive;
            area.UpdatedAt = DateTime.Now;
            await _db.SaveChangesAsync();

            return Json(new { success = true, isActive = area.IsActive });
        }

        [HttpGet]
        public async Task<IActionResult> Export()
        {
            var records = await _db.Areas
                .OrderBy(a => a.Name)
                .Select(a => new
                {
                    a.Code,
                    a.Name,
                    a.Description,
                    Active = a.IsActive ? "Yes" : "No",
                    CreatedAt = a.CreatedAt.ToString("dd MMM yyyy")
                })
                .ToListAsync();

            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add("Areas");

            ws.Cell(1, 1).Value = "Code";
            ws.Cell(1, 2).Value = "Name";
            ws.Cell(1, 3).Value = "Description";
            ws.Cell(1, 4).Value = "Active";
            ws.Cell(1, 5).Value = "Created At";

            int row = 2;
            foreach (var r in records)
            {
                ws.Cell(row, 1).Value = r.Code;
                ws.Cell(row, 2).Value = r.Name;
                ws.Cell(row, 3).Value = r.Description;
                ws.Cell(row, 4).Value = r.Active;
                ws.Cell(row, 5).Value = r.CreatedAt;
                row++;
            }

            using var stream = new MemoryStream();
            wb.SaveAs(stream);
            return File(stream.ToArray(),
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                "Areas.xlsx");
        }
    }
}
