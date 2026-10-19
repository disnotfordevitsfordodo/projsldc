using Dapper;
using Microsoft.AspNetCore.Mvc;
using MySqlConnector;
using SLDC.Web.Data;
using SLDC.Web.Models;

namespace SLDC.Web.Controllers;

// ทรัพยากรสุขภาพ (health_resource): โรงพยาบาล / รพ.สต. / อสม. / แพทย์ประจำ  เพิ่ม แก้ไข ลบ
public class HealthResourceController : AppController
{
    private const int PageSize = 15;
    public static readonly string[] Types = { "hospital", "rpst", "osm", "doctor" };
    public HealthResourceController(Db db) : base(db) { }

    // รายการที่ไม่ผูกตำบล (tumbol_id เป็นค่าว่าง) แก้ได้เฉพาะผู้ดูแลระบบ
    private async Task<bool> TumbolAllowedAsync(System.Data.IDbConnection c, string? tumbolId)
    {
        if (IsAdmin) return true;
        if (tumbolId == null) return false;
        var (a, t) = Scope(null, null);
        return await c.ExecuteScalarAsync<long>(@"SELECT COUNT(*) FROM m_tumbol tb WHERE tb.tumbol_id=@tumbolId
            AND (@a IS NULL OR tb.amphur_id=@a) AND (@t IS NULL OR tb.tumbol_id=@t)", new { tumbolId, a, t }) > 0;
    }

    public async Task<IActionResult> Index(string? type = null, string? amphurId = null, string? tumbolId = null, string? q = null, int page = 1)
    {
        using var c = _db.Open();
        var (amp, tum) = Scope(amphurId, tumbolId); q = Norm(q);
        type = Types.Contains(type ?? "") ? type : null;
        var p = new DynamicParameters();
        var w = new List<string> { "1=1" };
        if (type != null) { w.Add("r.resource_type=@type"); p.Add("type", type); }
        if (amp != null) { w.Add("tb.amphur_id=@amp"); p.Add("amp", amp); }
        if (tum != null) { w.Add("tb.tumbol_id=@tum"); p.Add("tum", tum); }
        if (q != null) { w.Add("r.name LIKE @q"); p.Add("q", "%" + q + "%"); }
        var from = @"FROM health_resource r LEFT JOIN m_tumbol tb ON tb.tumbol_id = r.tumbol_id
            LEFT JOIN m_amphur a ON a.amphur_id = tb.amphur_id WHERE " + string.Join(" AND ", w);
        var total = await c.ExecuteScalarAsync<long>("SELECT COUNT(*) " + from, p);
        var (pg, pages) = PageInfo(total, page, PageSize);
        p.Add("size", PageSize); p.Add("off", (pg - 1) * PageSize);
        var vm = new ResourceListVm
        {
            Type = type,
            Filter = new FilterVm { Action = "Index", AmphurId = amp, TumbolId = tum, Q = q, Amphurs = await Amphurs(c), Tumbols = await Tumbols(c, amp) },
            Rows = new Paged<ResourceRow>
            {
                Page = pg, Pages = pages, Total = total,
                Items = (await c.QueryAsync<ResourceRow>(@"SELECT r.resource_id, r.resource_type, r.name, r.tumbol_id,
                        tb.name_th AS tumbol_name, a.name_th AS amphur_name " + from + " ORDER BY r.resource_type, r.name LIMIT @size OFFSET @off", p)).ToList()
            }
        };
        return View(vm);
    }

    private async Task<ResourceFormVm> BuildVmAsync(System.Data.IDbConnection c, ResourceForm f, string? err)
    {
        var (a, t) = await AllowedAreaAsync(c);
        if (a != null && string.IsNullOrEmpty(f.AmphurId)) f.AmphurId = a;
        if (t != null && string.IsNullOrEmpty(f.TumbolId)) f.TumbolId = t;
        return new ResourceFormVm
        {
            Form = f, Error = err,
            Amphurs = (await Amphurs(c)).Where(x => a == null || x.Id == a).ToList(),
            Tumbols = (await Tumbols(c, Norm(f.AmphurId))).Where(x => t == null || x.Id == t).ToList()
        };
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int? id)
    {
        using var c = _db.Open();
        var f = new ResourceForm();
        if (id != null)
        {
            var r = await c.QueryFirstOrDefaultAsync<ResourceForm>(@"SELECT r.resource_id, r.resource_type, r.name, r.tumbol_id, tb.amphur_id
                FROM health_resource r LEFT JOIN m_tumbol tb ON tb.tumbol_id=r.tumbol_id WHERE r.resource_id=@id", new { id });
            if (r == null) return NotFound();
            if (!await TumbolAllowedAsync(c, r.TumbolId)) return Forbid();
            f = r;
        }
        return View(await BuildVmAsync(c, f, null));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(ResourceForm f)
    {
        using var c = _db.Open();
        f.Name = (f.Name ?? "").Trim(); f.TumbolId = Norm(f.TumbolId); f.AmphurId = Norm(f.AmphurId);
        string? err = null;
        if (!Types.Contains(f.ResourceType)) err = "ประเภทไม่ถูกต้อง";
        else if (f.Name.Length == 0) err = "กรุณากรอกชื่อ";
        else if (f.Name.Length > 200) err = "ชื่อต้องไม่เกิน 200 ตัวอักษร";
        else if (!IsAdmin && f.TumbolId == null) err = "กรุณาเลือกตำบล";
        else if (f.TumbolId != null && await c.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM m_tumbol WHERE tumbol_id=@t AND is_active=1", new { t = f.TumbolId }) == 0) err = "ตำบลไม่ถูกต้อง";
        else if (!await TumbolAllowedAsync(c, f.TumbolId)) err = "ตำบลที่เลือกอยู่นอกพื้นที่ที่คุณมีสิทธิ์";

        if (err == null && f.ResourceId != 0)
        {
            var old = await c.ExecuteScalarAsync<string?>("SELECT tumbol_id FROM health_resource WHERE resource_id=@id", new { id = f.ResourceId });
            if (!await TumbolAllowedAsync(c, old)) return Forbid();
        }
        if (err == null)
        {
            try
            {
                if (f.ResourceId == 0)
                    await c.ExecuteAsync("INSERT INTO health_resource(resource_type, name, tumbol_id) VALUES(@ResourceType, @Name, @TumbolId)", f);
                else
                    await c.ExecuteAsync("UPDATE health_resource SET resource_type=@ResourceType, name=@Name, tumbol_id=@TumbolId WHERE resource_id=@ResourceId", f);
                await Log(c, f.ResourceId == 0 ? "CREATE_RESOURCE" : "UPDATE_RESOURCE", f.ResourceId.ToString(), f.Name);
                Flash("บันทึกทรัพยากรสุขภาพเรียบร้อยแล้ว");
                return RedirectToAction("Index", new { type = f.ResourceType });
            }
            catch (MySqlException ex) { err = "บันทึกไม่ได้: " + ex.Message; }
        }
        return View(await BuildVmAsync(c, f, err));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        using var c = _db.Open();
        var exists = await c.QueryFirstOrDefaultAsync<ResourceForm>("SELECT resource_id, tumbol_id FROM health_resource WHERE resource_id=@id", new { id });
        if (exists == null) return NotFound();
        if (!await TumbolAllowedAsync(c, exists.TumbolId)) return Forbid();
        try
        {
            await c.ExecuteAsync("DELETE FROM health_resource WHERE resource_id=@id", new { id });
            await Log(c, "DELETE_RESOURCE", id.ToString());
            Flash("ลบทรัพยากรสุขภาพเรียบร้อยแล้ว");
        }
        catch (MySqlException ex) { FlashErr("ลบไม่ได้: " + ex.Message); }
        return RedirectToAction("Index");
    }
}
