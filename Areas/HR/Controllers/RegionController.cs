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
    public class RegionController : Controller
    {
        private readonly ServiceHubContext _db;
        private readonly ILogger<RegionController> _logger;

        public RegionController(ServiceHubContext db, ILogger<RegionController> logger)
        {
            _db = db;
            _logger = logger;
        }

        public IActionResult Index() => View();

        [HttpPost]
        public async Task<IActionResult> GetRegions()
        {
            var form = HttpContext.Request.Form;
            var draw = form["draw"].FirstOrDefault();
            var start = Convert.ToInt32(form["start"].FirstOrDefault() ?? "0");
            var length = Convert.ToInt32(form["length"].FirstOrDefault() ?? "10");
            var searchValue = form["search[value]"].FirstOrDefault();
            var sortColIndex = form["order[0][column]"].FirstOrDefault();
            var sortColName = form["columns[" + sortColIndex + "][name]"].FirstOrDefault();
            var sortDirection = form["order[0][dir]"].FirstOrDefault();

            var query = _db.Regions.AsQueryable();

            if (!string.IsNullOrEmpty(searchValue))
            {
                var s = searchValue.ToLower();
                query = query.Where(r =>
                    r.Code.Contains(searchValue) ||
                    r.Name.Contains(searchValue) ||
                    (r.Description ?? "").Contains(searchValue) ||
                    (s == "yes" && r.IsActive) ||
                    (s == "no" && !r.IsActive));
            }

            query = sortColName switch
            {
                "code"        => sortDirection == "asc" ? query.OrderBy(r => r.Code)        : query.OrderByDescending(r => r.Code),
                "name"        => sortDirection == "asc" ? query.OrderBy(r => r.Name)        : query.OrderByDescending(r => r.Name),
                "description" => sortDirection == "asc" ? query.OrderBy(r => r.Description) : query.OrderByDescending(r => r.Description),
                "isActive"    => sortDirection == "asc" ? query.OrderBy(r => r.IsActive)    : query.OrderByDescending(r => r.IsActive),
                _             => query.OrderBy(r => r.Name)
            };

            int total = await query.CountAsync();

            var data = await query.Skip(start).Take(length)
                .Select(r => new
                {
                    r.Id,
                    r.Code,
                    r.Name,
                    r.Description,
                    r.IsActive,
                    createdAt = r.CreatedAt.ToString("dd MMM yyyy")
                })
                .ToListAsync();

            return Json(new { draw, recordsTotal = total, recordsFiltered = total, data });
        }

        public IActionResult Create() => View(new Region());

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(Region model)
        {
            if (!ModelState.IsValid) return View(model);

            if (await _db.Regions.AnyAsync(r => r.Code.ToLower() == model.Code.ToLower()))
            {
                ModelState.AddModelError("Code", "A region with this code already exists.");
                return View(model);
            }

            model.CreatedAt = DateTime.Now;
            _db.Regions.Add(model);
            await _db.SaveChangesAsync();

            TempData["Success"] = $"Region '{model.Name}' created successfully.";
            return RedirectToAction(nameof(Index));
        }

        public async Task<IActionResult> Edit(int id)
        {
            var region = await _db.Regions.FindAsync(id);
            if (region == null) return NotFound();
            return View(region);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, Region model)
        {
            if (id != model.Id) return NotFound();
            if (!ModelState.IsValid) return View(model);

            if (await _db.Regions.AnyAsync(r => r.Code.ToLower() == model.Code.ToLower() && r.Id != model.Id))
            {
                ModelState.AddModelError("Code", "Another region with this code already exists.");
                return View(model);
            }

            model.UpdatedAt = DateTime.Now;
            _db.Update(model);

            try { await _db.SaveChangesAsync(); }
            catch (DbUpdateConcurrencyException)
            {
                if (!await _db.Regions.AnyAsync(r => r.Id == id)) return NotFound();
                throw;
            }

            TempData["Success"] = $"Region '{model.Name}' updated successfully.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        public async Task<IActionResult> ToggleActive(int id)
        {
            var region = await _db.Regions.FindAsync(id);
            if (region == null) return NotFound(new { success = false });

            region.IsActive = !region.IsActive;
            region.UpdatedAt = DateTime.Now;
            await _db.SaveChangesAsync();

            return Json(new { success = true, isActive = region.IsActive });
        }

        [HttpGet]
        public async Task<IActionResult> Export()
        {
            var records = await _db.Regions
                .OrderBy(r => r.Name)
                .Select(r => new
                {
                    r.Code,
                    r.Name,
                    r.Description,
                    Active = r.IsActive ? "Yes" : "No",
                    CreatedAt = r.CreatedAt.ToString("dd MMM yyyy")
                })
                .ToListAsync();

            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add("Regions");

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
                "Regions.xlsx");
        }
    }
}
