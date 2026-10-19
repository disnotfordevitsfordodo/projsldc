using Dapper;
using Microsoft.AspNetCore.Mvc;
using SLDC.Web.Data;
using SLDC.Web.Models;

namespace SLDC.Web.Controllers;

// ตัวเลือกแบบเลือกต่อเนื่อง (JSON) สำหรับฟอร์มเพิ่ม/แก้ไขทุกหน้า: อำเภอ > ตำบล > หมู่บ้าน > บ้าน > บุคคล
public class LookupController : AppController
{
    public LookupController(Db db) : base(db) { }

    [HttpGet]
    public async Task<IActionResult> TumbolList(string? amphurId)
    {
        using var c = _db.Open();
        var (a, t) = Scope(null, null);
        amphurId = Norm(amphurId);
        if (amphurId == null || (a != null && a != amphurId && t == null)) return Json(new List<Opt>());
        var list = await c.QueryAsync<Opt>(@"SELECT tumbol_id AS Id, name_th AS Name FROM m_tumbol
            WHERE amphur_id=@amphurId AND is_active=1 AND (@t IS NULL OR tumbol_id=@t) ORDER BY tumbol_id", new { amphurId, t });
        return Json(list);
    }

    [HttpGet]
    public async Task<IActionResult> VillageList(string? tumbolId)
    {
        using var c = _db.Open();
        var (a, t) = Scope(null, null);
        tumbolId = Norm(tumbolId);
        if (tumbolId == null) return Json(new List<Opt>());
        var list = await c.QueryAsync<Opt>(@"SELECT v.village_id AS Id, CONCAT('หมู่ ', v.moo, ' ', v.name_th) AS Name
            FROM m_village v JOIN m_tumbol tb ON tb.tumbol_id=v.tumbol_id
            WHERE v.tumbol_id=@tumbolId AND v.is_active=1 AND (@a IS NULL OR tb.amphur_id=@a) AND (@t IS NULL OR tb.tumbol_id=@t)
            ORDER BY v.moo", new { tumbolId, a, t });
        return Json(list);
    }

    [HttpGet]
    public async Task<IActionResult> HouseList(string? villageId, int year)
    {
        using var c = _db.Open();
        if (!await VillageAllowedAsync(c, Norm(villageId))) return Json(new List<Opt>());
        return Json(await HouseOptsAsync(c, villageId!, year));
    }

    [HttpGet]
    public async Task<IActionResult> MemberList(long houseId)
    {
        using var c = _db.Open();
        if (!await HouseAllowedAsync(c, houseId)) return Json(new List<Opt>());
        return Json(await MemberOptsAsync(c, houseId));
    }
}
