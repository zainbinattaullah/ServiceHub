using ClosedXML.Excel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using ServiceHub.Data;
using ServiceHub.Areas.HR.Models;

namespace ServiceHub.Areas.HR.Controllers
{
    [Area("HR")]
    [Authorize]
    public class StoreController : Controller
    {
        private readonly ServiceHubContext _db;
        private readonly ILogger<StoreController> _logger;

        public StoreController(ServiceHubContext db, ILogger<StoreController> logger)
        {
            _db = db;
            _logger = logger;
        }

        public IActionResult Index() => View();

        [HttpPost]
        public async Task<IActionResult> GetStores()
        {
            var form = HttpContext.Request.Form;
            var draw = form["draw"].FirstOrDefault();
            var start = Convert.ToInt32(form["start"].FirstOrDefault() ?? "0");
            var length = Convert.ToInt32(form["length"].FirstOrDefault() ?? "10");
            var searchValue = form["search[value]"].FirstOrDefault();
            var sortColIndex = form["order[0][column]"].FirstOrDefault();
            var sortColName = form["columns[" + sortColIndex + "][name]"].FirstOrDefault();
            var sortDirection = form["order[0][dir]"].FirstOrDefault();

            var query = _db.Stores.Include(s => s.Area).Include(s => s.Region).AsQueryable();

            if (!string.IsNullOrEmpty(searchValue))
            {
                var s = searchValue.ToLower();
                query = query.Where(st =>
                    st.StoreCode.Contains(searchValue) ||
                    st.StoreName.Contains(searchValue) ||
                    (st.Area != null && st.Area.Name.Contains(searchValue)) ||
                    (st.Region != null && st.Region.Name.Contains(searchValue)) ||
                    (s == "yes" && st.IsActive) ||
                    (s == "no" && !st.IsActive));
            }

            query = sortColName switch
            {
                "storeCode" => sortDirection == "asc" ? query.OrderBy(st => st.StoreCode) : query.OrderByDescending(st => st.StoreCode),
                "storeName" => sortDirection == "asc" ? query.OrderBy(st => st.StoreName) : query.OrderByDescending(st => st.StoreName),
                "area"      => sortDirection == "asc" ? query.OrderBy(st => st.Area!.Name) : query.OrderByDescending(st => st.Area!.Name),
                "region"    => sortDirection == "asc" ? query.OrderBy(st => st.Region!.Name) : query.OrderByDescending(st => st.Region!.Name),
                "isActive"  => sortDirection == "asc" ? query.OrderBy(st => st.IsActive) : query.OrderByDescending(st => st.IsActive),
                _           => query.OrderBy(st => st.StoreName)
            };

            int total = await query.CountAsync();

            var data = await query.Skip(start).Take(length)
                .Select(st => new
                {
                    st.Id,
                    st.StoreCode,
                    st.StoreName,
                    area   = st.Area   != null ? st.Area.Name   : "",
                    region = st.Region != null ? st.Region.Name : "",
                    st.IsActive,
                    createdAt = st.CreatedAt.ToString("dd MMM yyyy")
                })
                .ToListAsync();

            return Json(new { draw, recordsTotal = total, recordsFiltered = total, data });
        }

        private async Task PopulateDropdowns(Store? model = null)
        {
            ViewBag.Areas   = new SelectList(await _db.Areas.Where(a => a.IsActive).OrderBy(a => a.Name).ToListAsync(),   "Id", "Name", model?.AreaId);
            ViewBag.Regions = new SelectList(await _db.Regions.Where(r => r.IsActive).OrderBy(r => r.Name).ToListAsync(), "Id", "Name", model?.RegionId);
        }

        public async Task<IActionResult> Create()
        {
            await PopulateDropdowns();
            return View(new Store());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(Store model)
        {
            if (!ModelState.IsValid) { await PopulateDropdowns(model); return View(model); }

            if (await _db.Stores.AnyAsync(s => s.StoreCode.ToLower() == model.StoreCode.ToLower()))
            {
                ModelState.AddModelError("StoreCode", "A store with this code already exists.");
                await PopulateDropdowns(model);
                return View(model);
            }

            model.CreatedAt = DateTime.Now;
            _db.Stores.Add(model);
            await _db.SaveChangesAsync();

            TempData["Success"] = $"Store '{model.StoreName}' created successfully.";
            return RedirectToAction(nameof(Index));
        }

        public async Task<IActionResult> Edit(int id)
        {
            var store = await _db.Stores.FindAsync(id);
            if (store == null) return NotFound();
            await PopulateDropdowns(store);
            return View(store);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, Store model)
        {
            if (id != model.Id) return NotFound();
            if (!ModelState.IsValid) { await PopulateDropdowns(model); return View(model); }

            if (await _db.Stores.AnyAsync(s => s.StoreCode.ToLower() == model.StoreCode.ToLower() && s.Id != model.Id))
            {
                ModelState.AddModelError("StoreCode", "Another store with this code already exists.");
                await PopulateDropdowns(model);
                return View(model);
            }

            model.UpdatedAt = DateTime.Now;
            _db.Update(model);

            try { await _db.SaveChangesAsync(); }
            catch (DbUpdateConcurrencyException)
            {
                if (!await _db.Stores.AnyAsync(s => s.Id == id)) return NotFound();
                throw;
            }

            TempData["Success"] = $"Store '{model.StoreName}' updated successfully.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        public async Task<IActionResult> ToggleActive(int id)
        {
            var store = await _db.Stores.FindAsync(id);
            if (store == null) return NotFound(new { success = false });

            store.IsActive = !store.IsActive;
            store.UpdatedAt = DateTime.Now;
            await _db.SaveChangesAsync();

            return Json(new { success = true, isActive = store.IsActive });
        }

        // Returns area/region info for a store — used by AttendanceMachine form cascade
        [HttpGet]
        public async Task<IActionResult> GetStoreInfo(int id)
        {
            var store = await _db.Stores
                .Include(s => s.Area)
                .Include(s => s.Region)
                .Where(s => s.Id == id)
                .Select(s => new
                {
                    s.AreaId,
                    AreaName   = s.Area   != null ? s.Area.Name   : "",
                    s.RegionId,
                    RegionName = s.Region != null ? s.Region.Name : ""
                })
                .FirstOrDefaultAsync();

            if (store == null) return NotFound();
            return Json(store);
        }

        [HttpGet]
        public async Task<IActionResult> Export()
        {
            var records = await _db.Stores
                .Include(s => s.Area)
                .Include(s => s.Region)
                .OrderBy(s => s.StoreName)
                .Select(s => new
                {
                    s.StoreCode,
                    s.StoreName,
                    Area       = s.Area   != null ? s.Area.Name   : "",
                    Region     = s.Region != null ? s.Region.Name : "",
                    Active    = s.IsActive ? "Yes" : "No",
                    CreatedAt = s.CreatedAt.ToString("dd MMM yyyy")
                })
                .ToListAsync();

            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add("Stores");

            ws.Cell(1, 1).Value = "Store Code";
            ws.Cell(1, 2).Value = "Store Name";
            ws.Cell(1, 3).Value = "Area";
            ws.Cell(1, 4).Value = "Region";
            ws.Cell(1, 5).Value = "Active";
            ws.Cell(1, 6).Value = "Created At";

            int row = 2;
            foreach (var r in records)
            {
                ws.Cell(row, 1).Value = r.StoreCode;
                ws.Cell(row, 2).Value = r.StoreName;
                ws.Cell(row, 3).Value = r.Area;
                ws.Cell(row, 4).Value = r.Region;
                ws.Cell(row, 5).Value = r.Active;
                ws.Cell(row, 6).Value = r.CreatedAt;
                row++;
            }

            using var stream = new MemoryStream();
            wb.SaveAs(stream);
            return File(stream.ToArray(),
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                "Stores.xlsx");
        }
    }
}
