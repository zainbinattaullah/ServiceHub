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
    public class DepartmentController : Controller
    {
        private readonly ServiceHubContext _db;
        private readonly ILogger<DepartmentController> _logger;

        public DepartmentController(ServiceHubContext db, ILogger<DepartmentController> logger)
        {
            _db = db;
            _logger = logger;
        }

        public IActionResult Index() => View();
        [HttpPost]
        public async Task<IActionResult> GetDepartments()
        {
            var form = HttpContext.Request.Form;
            var draw = form["draw"].FirstOrDefault();
            var start = Convert.ToInt32(form["start"].FirstOrDefault() ?? "0");
            var length = Convert.ToInt32(form["length"].FirstOrDefault() ?? "10");
            var searchValue = form["search[value]"].FirstOrDefault();
            var sortColIndex = form["order[0][column]"].FirstOrDefault();
            var sortColName = form["columns[" + sortColIndex + "][name]"].FirstOrDefault();
            var sortDirection = form["order[0][dir]"].FirstOrDefault();

            var query = _db.Departments.AsQueryable();

            if (!string.IsNullOrEmpty(searchValue))
            {
                var s = searchValue.ToLower();
                query = query.Where(d =>
                    d.Code.Contains(searchValue) ||
                    d.Name.Contains(searchValue) ||
                    (d.Description ?? "").Contains(searchValue) ||
                    (s == "yes" && d.IsActive) ||
                    (s == "no" && !d.IsActive));
            }

            query = sortColName switch
            {
                "code" => sortDirection == "asc" ? query.OrderBy(d => d.Code) : query.OrderByDescending(d => d.Code),
                "name" => sortDirection == "asc" ? query.OrderBy(d => d.Name) : query.OrderByDescending(d => d.Name),
                "description" => sortDirection == "asc" ? query.OrderBy(d => d.Description) : query.OrderByDescending(d => d.Description),
                "isActive" => sortDirection == "asc" ? query.OrderBy(d => d.IsActive) : query.OrderByDescending(d => d.IsActive),
                _ => query.OrderBy(d => d.Name)
            };

            int total = await query.CountAsync();

            var data = await query.Skip(start).Take(length)
                .Select(d => new
                {
                    d.Id,
                    d.Code,
                    d.Name,
                    d.Description,
                    d.IsActive,
                    createdAt = d.CreatedAt.ToString("dd MMM yyyy")
                })
                .ToListAsync();

            return Json(new { draw, recordsTotal = total, recordsFiltered = total, data });
        }
        // ── Create ───────────────────────────────────────────────────────
        public IActionResult Create() => View(new Department());

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(Department model)
        {
            if (!ModelState.IsValid) return View(model);

            bool codeExists = await _db.Departments
                .AnyAsync(d => d.Code.ToLower() == model.Code.ToLower());
            if (codeExists)
            {
                ModelState.AddModelError("Code", "A department with this code already exists.");
                return View(model);
            }

            model.CreatedAt = DateTime.Now;
            _db.Departments.Add(model);
            await _db.SaveChangesAsync();

            TempData["Success"] = $"Department '{model.Name}' created successfully.";
            return RedirectToAction(nameof(Index));
        }

        // ── Edit ─────────────────────────────────────────────────────────
        public async Task<IActionResult> Edit(int id)
        {
            var dept = await _db.Departments.FindAsync(id);
            if (dept == null) return NotFound();
            return View(dept);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, Department model)
        {
            if (id != model.Id) return NotFound();
            if (!ModelState.IsValid) return View(model);

            bool codeExists = await _db.Departments
                .AnyAsync(d => d.Code.ToLower() == model.Code.ToLower() && d.Id != model.Id);
            if (codeExists)
            {
                ModelState.AddModelError("Code", "Another department with this code already exists.");
                return View(model);
            }

            model.UpdatedAt = DateTime.Now;
            _db.Update(model);

            try
            {
                await _db.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!await _db.Departments.AnyAsync(d => d.Id == id)) return NotFound();
                throw;
            }

            TempData["Success"] = $"Department '{model.Name}' updated successfully.";
            return RedirectToAction(nameof(Index));
        }

        // ── Toggle Active ─────────────────────────────────────────────────
        [HttpPost]
        public async Task<IActionResult> ToggleActive(int id)
        {
            var dept = await _db.Departments.FindAsync(id);
            if (dept == null) return NotFound(new { success = false });

            dept.IsActive = !dept.IsActive;
            dept.UpdatedAt = DateTime.Now;
            await _db.SaveChangesAsync();

            return Json(new { success = true, isActive = dept.IsActive });
        }

        // ── Export Excel ─────────────────────────────────────────────────
        [HttpGet]
        public async Task<IActionResult> Export()
        {
            var records = await _db.Departments
                .OrderBy(d => d.Name)
                .Select(d => new
                {
                    d.Code,
                    d.Name,
                    d.Description,
                    Active = d.IsActive ? "Yes" : "No",
                    CreatedAt = d.CreatedAt.ToString("dd MMM yyyy")
                })
                .ToListAsync();

            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add("Departments");

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
                "Departments.xlsx");
        }
    }
}
