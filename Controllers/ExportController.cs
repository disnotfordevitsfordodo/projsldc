using System.IO.Compression;
using System.Text;
using Dapper;
using Microsoft.AspNetCore.Mvc;
using SLDC.Web.Data;
using SLDC.Web.Models;

namespace SLDC.Web.Controllers;

/// <summary>
/// ศูนย์ส่งออกข้อมูล แยกตามผู้ใช้ปลายทาง
///  1) เจ้าหน้าที่ภาคสนาม  → ใบรายชื่อลงพื้นที่ (พิมพ์ / CSV) ซึ่งมีชื่อจริง จำกัดตามสิทธิ์พื้นที่
///  2) นักวิจัย/นักวิเคราะห์ → ชุดข้อมูลไม่ระบุตัวตน CSV / JSON
///  3) หน่วยงานอื่น/นักออกแบบระบบ → แพ็กเกจ ZIP + พจนานุกรมข้อมูล + JSON (ใช้ endpoint เดียวกับข้อ 2)
///  4) ประชาชนทั่วไป → ดูที่ OpenDataController (ไม่ต้องล็อกอิน)
/// </summary>
public class ExportController : AppController
{
    private readonly IConfiguration _cfg;
    public ExportController(Db db, IConfiguration cfg) : base(db) => _cfg = cfg;

    // salt ลับสำหรับสร้างรหัสสุ่ม ตั้งค่าใน appsettings.json > Export:Salt (ควรเปลี่ยนก่อนใช้งานจริง และห้ามเปิดเผย)
    private string Salt => _cfg["Export:Salt"] ?? "sldc-change-this-salt";

    public async Task<IActionResult> Index(int year = 0, string? amphurId = null, string? tumbolId = null)
    {
        using var c = _db.Open();
        year = await _db.YearAsync(c, year);
        var picker = await BuildPickerAsync(c, new PickerVm { Year = year, AmphurId = Norm(amphurId), TumbolId = Norm(tumbolId) });
        var years = (await c.QueryAsync<int>("SELECT DISTINCT survey_year FROM t_house ORDER BY survey_year DESC")).ToList();
        if (!years.Contains(year)) years.Insert(0, year);
        var (a, t) = Scope(null, null);
        return View(new ExportVm
        {
            Year = year, Years = years, Picker = picker, IsAdmin = IsAdmin,
            ScopeNote = t != null ? "บัญชีของคุณเห็นเฉพาะตำบลที่รับผิดชอบ" : a != null ? "บัญชีของคุณเห็นเฉพาะอำเภอที่รับผิดชอบ" : null
        });
    }

    // ---------------- 1) เจ้าหน้าที่ภาคสนาม ----------------
    [HttpGet]
    public async Task<IActionResult> Field(int year = 0, string? villageId = null, string format = "print")
    {
        villageId = Norm(villageId);
        using var c = _db.Open();
        if (!await VillageAllowedAsync(c, villageId))
        {
            FlashErr("กรุณาเลือกหมู่บ้านที่อยู่ในพื้นที่ของคุณ");
            return RedirectToAction("Index");
        }
        year = await _db.YearAsync(c, year);
        var vm = await Roster.ByVillageAsync(c, villageId!, year);
        await Log(c, "EXPORT_FIELD", villageId, $"year={year} format={format} houses={vm.Houses.Count}");

        if (format != "csv") return View("FieldSheet", vm);

        var sb = new StringBuilder();
        sb.Append(ExportWriter.CsvLine(new object[] {
            "house_id", "รหัสบ้าน", "บ้านเลขที่", "จุดสังเกต", "member_id", "ชื่อ-นามสกุล", "เลขประจำตัว (ปิดบังบางส่วน)", "อายุ", "เพศ",
            "ความสัมพันธ์", "อาชีพเดิม", "สถานะบัตรเดิม", "พิการ (เดิม)", "ป่วยเรื้อรัง (เดิม)",
            "สถานะที่พบ (อยู่/ย้ายออก/เสียชีวิต/ไม่พบ)", "อาชีพปัจจุบัน", "รายได้/เดือน (บาท)", "ถือบัตรสวัสดิการ (ใช่/ไม่)", "หมายเหตุ" }));
        foreach (var h in vm.Houses)
        {
            if (h.Members.Count == 0)
                sb.Append(ExportWriter.CsvLine(new object?[] { h.HouseId, h.HouseCode, h.HouseNo, h.Nearby, null, "(ยังไม่มีสมาชิกในระบบ)" }));
            foreach (var m in h.Members)
                sb.Append(ExportWriter.CsvLine(new object?[] { h.HouseId, h.HouseCode, h.HouseNo, h.Nearby, m.MemberId, m.FullName?.Trim(), m.CitizenMasked, m.Age, m.Gender,
                    m.Relationship, m.Occupation, m.WelfareName, m.IsDisabled == true ? "ใช่" : "ไม่", m.IsChronicPatient == true ? "ใช่" : "ไม่" }));
        }
        return File(ExportWriter.CsvBytes(sb.ToString()), "text/csv; charset=utf-8", $"sldc_field_roster_{villageId}_{year}.csv");
    }

    // ---------------- 2) + 3) ชุดข้อมูลไม่ระบุตัวตน (CSV / JSON / ZIP) ----------------
    // ต้องระบุหน่วยงาน/วัตถุประสงค์ และยืนยันเงื่อนไข — บันทึกใน web_system_logs ทุกครั้ง
    // ใช้เป็น API ได้: /Export/Download?key=members&format=json&year=2569&amphurId=4701&purpose=...&consent=true&limit=50000&offset=0
    [HttpGet]
    public async Task<IActionResult> Download(string key, string format = "csv", int year = 0, string? amphurId = null, string? tumbolId = null,
                                              string? purpose = null, bool consent = false, int? limit = null, int offset = 0)
    {
        format = format == "json" ? "json" : "csv";
        var isPackage = key == "package";
        var ds = isPackage ? null : ExportCatalog.Private.FirstOrDefault(d => d.Key == key);
        if (!isPackage && ds == null) return NotFound();

        purpose = Norm(purpose);
        if (purpose == null || purpose.Length < 5 || !consent)
        {
            FlashErr("กรุณาระบุหน่วยงาน/วัตถุประสงค์การใช้ข้อมูล และยืนยันเงื่อนไขก่อนดาวน์โหลด");
            return RedirectToAction("Index", new { year, amphurId, tumbolId });
        }
        if (purpose.Length > 300) purpose = purpose[..300];

        using var c = _db.Open();
        year = await _db.YearAsync(c, year);
        var (a, t) = Scope(amphurId, tumbolId);
        var prm = new Dictionary<string, object?> { ["salt"] = Salt, ["year"] = year, ["a"] = a, ["t"] = t };
        var stamp = DateTime.Now.ToString("yyyyMMdd");
        await Log(c, "EXPORT", key, $"year={year} area={a ?? "-"}/{t ?? "-"} format={(isPackage ? "zip" : format)} purpose={purpose}");

        if (isPackage)
        {
            // ZipArchive ต้องเขียนแบบ synchronous จึงเขียนลงไฟล์ชั่วคราวก่อนแล้วค่อยส่ง (ลบอัตโนมัติเมื่อส่งเสร็จ)
            var tmp = new FileStream(Path.GetTempFileName(), FileMode.Create, FileAccess.ReadWrite, FileShare.None, 81920, FileOptions.DeleteOnClose);
            using (var zip = new ZipArchive(tmp, ZipArchiveMode.Create, leaveOpen: true))
            {
                foreach (var d in ExportCatalog.Private)
                {
                    var entry = zip.CreateEntry($"datasets/{d.Key}.csv", CompressionLevel.Fastest);
                    await using var es = entry.Open();
                    await ExportWriter.WriteAsync(c, d, prm, false, es, "csv", HttpContext.RequestAborted);
                }
                var scopeText = tumbolId != null || amphurId != null ? $"อำเภอ {amphurId ?? "-"} ตำบล {tumbolId ?? "-"}" : null;
                await AddTextAsync(zip, "data_dictionary.csv", ExportWriter.DictionaryCsv(ExportCatalog.Private), bom: true);
                await AddTextAsync(zip, "README.txt", ExportCatalog.Readme(year, scopeText, purpose), bom: true);
            }
            tmp.Position = 0;
            return File(tmp, "application/zip", $"sldc_package_{year}_{stamp}.zip");
        }

        var paged = limit != null;
        if (paged) { prm["lim"] = Math.Clamp(limit!.Value, 1, 500_000); prm["off"] = Math.Max(0, offset); }
        Response.ContentType = format == "json" ? "application/json; charset=utf-8" : "text/csv; charset=utf-8";
        Response.Headers["Content-Disposition"] = $"attachment; filename=\"sldc_{ds!.Key}_{year}_{stamp}.{format}\"";
        await ExportWriter.WriteAsync(c, ds, prm, paged, Response.Body, format, HttpContext.RequestAborted);
        return new EmptyResult();
    }

    [HttpGet]
    public IActionResult Dictionary() =>
        File(ExportWriter.CsvBytes(ExportWriter.DictionaryCsv(ExportCatalog.All)), "text/csv; charset=utf-8", "sldc_data_dictionary.csv");

    private static async Task AddTextAsync(ZipArchive zip, string name, string text, bool bom)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        await using var s = entry.Open();
        var bytes = bom ? ExportWriter.CsvBytes(text) : Encoding.UTF8.GetBytes(text);
        await s.WriteAsync(bytes);
    }
}
