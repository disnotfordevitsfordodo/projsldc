using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SLDC.Web.Data;
using SLDC.Web.Models;

namespace SLDC.Web.Controllers;

/// <summary>ข้อมูลเปิดสำหรับประชาชนทั่วไป — เฉพาะสถิติสรุปรวม ไม่ระบุตัวบุคคล เข้าถึงได้โดยไม่ต้องล็อกอิน</summary>
[AllowAnonymous]
public class OpenDataController : Controller
{
    private readonly Db _db;
    public OpenDataController(Db db) => _db = db;

    public async Task<IActionResult> Index(int year = 0)
    {
        using var c = _db.Open();
        year = await _db.YearAsync(c, year);
        ViewBag.Year = year;
        return View(ExportCatalog.Public);
    }

    [HttpGet]
    public async Task<IActionResult> Get(string key, string format = "csv", int year = 0)
    {
        var ds = ExportCatalog.Public.FirstOrDefault(d => d.Key == key);
        if (ds == null) return NotFound();
        format = format == "json" ? "json" : "csv";
        using var c = _db.Open();
        year = await _db.YearAsync(c, year);
        Response.ContentType = format == "json" ? "application/json; charset=utf-8" : "text/csv; charset=utf-8";
        Response.Headers["Content-Disposition"] = $"attachment; filename=\"sldc_{ds.Key}_{year}.{format}\"";
        await ExportWriter.WriteAsync(c, ds, new Dictionary<string, object?> { ["year"] = year }, false, Response.Body, format, HttpContext.RequestAborted);
        return new EmptyResult();
    }

    [HttpGet]
    public IActionResult Dictionary() =>
        File(ExportWriter.CsvBytes(ExportWriter.DictionaryCsv(ExportCatalog.Public)), "text/csv; charset=utf-8", "sldc_open_data_dictionary.csv");
}
