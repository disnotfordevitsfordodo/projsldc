using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MySqlConnector;
using SLDC.Web.Data;
using SLDC.Web.Models;

namespace SLDC.Web.Controllers;

[Authorize(Roles = "admin")]
public class LocationsController : AppController
{
    private sealed class DupException : Exception { }
    public LocationsController(Db db) : base(db) { }

    public async Task<IActionResult> Index(string tab = "village", string? amphurId = null, string? tumbolId = null, string? q = null, int page = 1)
    {
        using var c = _db.Open();
        amphurId = Norm(amphurId); tumbolId = Norm(tumbolId); q = Norm(q);
        tab = tab == "tumbol" || tab == "amphur" ? tab : "village";
        var vm = new LocVm
        {
            Tab = tab,
            Filter = new FilterVm { Action = "Index", Tab = tab, AmphurId = amphurId, TumbolId = tumbolId, Q = q,
                                    Amphurs = await Amphurs(c), Tumbols = await Tumbols(c, amphurId) }
        };
        var p = new DynamicParameters();
        if (tab == "village")
        {
            var w = new List<string> { "v.is_active=1" };
            if (amphurId != null) { w.Add("a.amphur_id=@amp"); p.Add("amp", amphurId); }
            if (tumbolId != null) { w.Add("t.tumbol_id=@tum"); p.Add("tum", tumbolId); }
            if (q != null) { w.Add("v.name_th LIKE @q"); p.Add("q", "%" + q + "%"); }
            var fromSql = @"FROM m_village v JOIN m_tumbol t ON t.tumbol_id=v.tumbol_id
                JOIN m_amphur a ON a.amphur_id=t.amphur_id JOIN m_province pr ON pr.province_id=a.province_id
                WHERE " + string.Join(" AND ", w);
            var total = await c.ExecuteScalarAsync<long>("SELECT COUNT(*) " + fromSql, p);
            var (pg, pages) = PageInfo(total, page, 10);
            p.Add("size", 10); p.Add("off", (pg - 1) * 10);
            vm.Villages = new Paged<VillageRow>
            {
                Page = pg, Pages = pages, Total = total,
                Items = (await c.QueryAsync<VillageRow>(@"SELECT v.village_id, pr.name_th AS province_name, a.name_th AS amphur_name,
                    t.name_th AS tumbol_name, v.moo, v.name_th " + fromSql + " ORDER BY v.village_id LIMIT @size OFFSET @off", p)).ToList()
            };
        }
        else if (tab == "amphur")
        {
            var w = new List<string> { "a.is_active=1" };
            if (q != null) { w.Add("(a.name_th LIKE @q OR a.amphur_id LIKE @q)"); p.Add("q", "%" + q + "%"); }
            var fromSql = "FROM m_amphur a WHERE " + string.Join(" AND ", w);
            var total = await c.ExecuteScalarAsync<long>("SELECT COUNT(*) " + fromSql, p);
            var (pg, pages) = PageInfo(total, page, 10);
            p.Add("size", 10); p.Add("off", (pg - 1) * 10);
            vm.Amphurs = new Paged<AmphurRow>
            {
                Page = pg, Pages = pages, Total = total,
                Items = (await c.QueryAsync<AmphurRow>("SELECT a.amphur_id, a.name_th, a.name_en " + fromSql + " ORDER BY a.amphur_id LIMIT @size OFFSET @off", p)).ToList()
            };
        }
        else
        {
            var w = new List<string> { "t.is_active=1" };
            if (amphurId != null) { w.Add("a.amphur_id=@amp"); p.Add("amp", amphurId); }
            if (q != null) { w.Add("t.name_th LIKE @q"); p.Add("q", "%" + q + "%"); }
            var fromSql = "FROM m_tumbol t JOIN m_amphur a ON a.amphur_id=t.amphur_id WHERE " + string.Join(" AND ", w);
            var total = await c.ExecuteScalarAsync<long>("SELECT COUNT(*) " + fromSql, p);
            var (pg, pages) = PageInfo(total, page, 10);
            p.Add("size", 10); p.Add("off", (pg - 1) * 10);
            vm.Tumbols = new Paged<TumbolRow>
            {
                Page = pg, Pages = pages, Total = total,
                Items = (await c.QueryAsync<TumbolRow>(@"SELECT t.tumbol_id, a.name_th AS amphur_name, t.name_th, t.post_code, t.target "
                    + fromSql + " ORDER BY t.tumbol_id LIMIT @size OFFSET @off", p)).ToList()
            };
        }
        return View(vm);
    }

    private async Task<List<Opt>> AllTumbols(System.Data.IDbConnection c) =>
        (await c.QueryAsync<Opt>(@"SELECT t.tumbol_id AS Id, CONCAT('ต.', t.name_th, ' (อ.', a.name_th, ')') AS Name
            FROM m_tumbol t JOIN m_amphur a ON a.amphur_id = t.amphur_id WHERE t.is_active=1 ORDER BY t.tumbol_id")).ToList();

    // ---------- หมู่บ้าน ----------
    [HttpGet]
    public async Task<IActionResult> VillageEdit(string? id)
    {
        using var c = _db.Open();
        var f = new VillageForm { IsNew = true };
        if (id != null)
        {
            var v = await c.QueryFirstOrDefaultAsync<VillageForm>(
                "SELECT village_id, tumbol_id, moo, name_th, target FROM m_village WHERE village_id=@id", new { id });
            if (v == null) return NotFound();
            f = v; f.IsNew = false;
        }
        return View(new VillageFormVm { Form = f, Tumbols = await AllTumbols(c) });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> VillageEdit(VillageForm f)
    {
        using var c = _db.Open();
        f.VillageId = (f.VillageId ?? "").Trim(); f.NameTh = (f.NameTh ?? "").Trim();
        string? err = null;
        if (f.VillageId.Length < 6 || f.VillageId.Length > 12) err = "รหัสหมู่บ้านต้องยาว 6-12 หลัก";
        else if (string.IsNullOrEmpty(f.TumbolId)) err = "กรุณาเลือกตำบล";
        else if (f.NameTh.Length == 0) err = "กรุณากรอกชื่อหมู่บ้าน";
        else if (f.Moo <= 0) err = "หมู่ที่ต้องมากกว่า 0";
        if (err == null)
        {
            try
            {
                if (f.IsNew)
                {
                    // ถ้ารหัสนี้เคยถูกลบ (ซ่อน) ไว้ ให้กู้คืนพร้อมอัปเดตข้อมูล แทนการแจ้งว่าซ้ำ
                    var old = await c.ExecuteScalarAsync<int?>("SELECT is_active FROM m_village WHERE village_id=@VillageId", f);
                    if (old == 1) throw new DupException();
                    if (old == 0)
                        await c.ExecuteAsync("UPDATE m_village SET tumbol_id=@TumbolId, moo=@Moo, name_th=@NameTh, target=@Target, is_active=1 WHERE village_id=@VillageId", f);
                    else
                        await c.ExecuteAsync("INSERT INTO m_village(village_id,tumbol_id,moo,name_th,target,is_active) VALUES(@VillageId,@TumbolId,@Moo,@NameTh,@Target,1)", f);
                }
                else
                    await c.ExecuteAsync("UPDATE m_village SET tumbol_id=@TumbolId, moo=@Moo, name_th=@NameTh, target=@Target WHERE village_id=@VillageId", f);
                await Log(c, f.IsNew ? "CREATE_VILLAGE" : "UPDATE_VILLAGE", f.VillageId);
                Flash("บันทึกข้อมูลหมู่บ้านเรียบร้อยแล้ว");
                return RedirectToAction("Index", new { tab = "village" });
            }
            catch (DupException) { err = "รหัสหมู่บ้านนี้มีอยู่แล้ว"; }
            catch (MySqlException ex)
            {
                err = ex.ErrorCode == MySqlErrorCode.DuplicateKeyEntry ? "รหัสหมู่บ้านนี้มีอยู่แล้ว" : "บันทึกไม่ได้: " + ex.Message;
            }
        }
        return View(new VillageFormVm { Form = f, Error = err, Tumbols = await AllTumbols(c) });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> VillageDelete(string id)
    {
        using var c = _db.Open();
        if (await c.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM t_house WHERE village_id=@id AND is_active=1", new { id }) > 0)
        { FlashErr("ลบไม่ได้ เพราะหมู่บ้านนี้ยังมีครัวเรือนอยู่ กรุณาย้ายหรือลบครัวเรือนก่อน"); return RedirectToAction("Index", new { tab = "village" }); }
        await c.ExecuteAsync("UPDATE m_village SET is_active=0 WHERE village_id=@id", new { id });   // soft delete เพราะมี FK
        await Log(c, "DELETE_VILLAGE", id);
        Flash("ลบหมู่บ้านเรียบร้อยแล้ว");
        return RedirectToAction("Index", new { tab = "village" });
    }

    // ---------- ตำบล ----------
    [HttpGet]
    public async Task<IActionResult> TumbolEdit(string? id)
    {
        using var c = _db.Open();
        var f = new TumbolForm { IsNew = true };
        if (id != null)
        {
            var t = await c.QueryFirstOrDefaultAsync<TumbolForm>(
                "SELECT tumbol_id, amphur_id, name_th, post_code, target FROM m_tumbol WHERE tumbol_id=@id", new { id });
            if (t == null) return NotFound();
            f = t; f.IsNew = false;
        }
        return View(new TumbolFormVm { Form = f, Amphurs = await Amphurs(c) });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> TumbolEdit(TumbolForm f)
    {
        using var c = _db.Open();
        f.TumbolId = (f.TumbolId ?? "").Trim(); f.NameTh = (f.NameTh ?? "").Trim(); f.PostCode = Norm(f.PostCode);
        string? err = null;
        if (f.TumbolId.Length != 6) err = "รหัสตำบลต้องยาว 6 หลัก";
        else if (string.IsNullOrEmpty(f.AmphurId)) err = "กรุณาเลือกอำเภอ";
        else if (f.NameTh.Length == 0) err = "กรุณากรอกชื่อตำบล";
        if (err == null)
        {
            try
            {
                if (f.IsNew)
                {
                    var old = await c.ExecuteScalarAsync<int?>("SELECT is_active FROM m_tumbol WHERE tumbol_id=@TumbolId", f);
                    if (old == 1) throw new DupException();
                    if (old == 0)
                        await c.ExecuteAsync("UPDATE m_tumbol SET amphur_id=@AmphurId, name_th=@NameTh, post_code=@PostCode, target=@Target, is_active=1 WHERE tumbol_id=@TumbolId", f);
                    else
                        await c.ExecuteAsync("INSERT INTO m_tumbol(tumbol_id,amphur_id,name_th,post_code,target,is_active) VALUES(@TumbolId,@AmphurId,@NameTh,@PostCode,@Target,1)", f);
                }
                else
                    await c.ExecuteAsync("UPDATE m_tumbol SET amphur_id=@AmphurId, name_th=@NameTh, post_code=@PostCode, target=@Target WHERE tumbol_id=@TumbolId", f);
                await Log(c, f.IsNew ? "CREATE_TUMBOL" : "UPDATE_TUMBOL", f.TumbolId);
                Flash("บันทึกข้อมูลตำบลเรียบร้อยแล้ว");
                return RedirectToAction("Index", new { tab = "tumbol" });
            }
            catch (DupException) { err = "รหัสตำบลนี้มีอยู่แล้ว"; }
            catch (MySqlException ex)
            {
                err = ex.ErrorCode == MySqlErrorCode.DuplicateKeyEntry ? "รหัสตำบลนี้มีอยู่แล้ว" : "บันทึกไม่ได้: " + ex.Message;
            }
        }
        return View(new TumbolFormVm { Form = f, Error = err, Amphurs = await Amphurs(c) });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> TumbolDelete(string id)
    {
        using var c = _db.Open();
        if (await c.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM m_village WHERE tumbol_id=@id AND is_active=1", new { id }) > 0)
        { FlashErr("ลบไม่ได้ เพราะตำบลนี้ยังมีหมู่บ้านอยู่ กรุณาลบหมู่บ้านก่อน"); return RedirectToAction("Index", new { tab = "tumbol" }); }
        await c.ExecuteAsync("UPDATE m_tumbol SET is_active=0 WHERE tumbol_id=@id", new { id });
        await Log(c, "DELETE_TUMBOL", id);
        Flash("ลบตำบลเรียบร้อยแล้ว");
        return RedirectToAction("Index", new { tab = "tumbol" });
    }

    // ---------- อำเภอ ----------
    [HttpGet]
    public async Task<IActionResult> AmphurEdit(string? id)
    {
        using var c = _db.Open();
        var f = new AmphurForm { IsNew = true };
        if (id != null)
        {
            var a = await c.QueryFirstOrDefaultAsync<AmphurForm>("SELECT amphur_id, name_th, name_en FROM m_amphur WHERE amphur_id=@id AND is_active=1", new { id });
            if (a == null) return NotFound();
            f = a; f.IsNew = false;
        }
        return View(new AmphurFormVm { Form = f });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> AmphurEdit(AmphurForm f)
    {
        using var c = _db.Open();
        f.AmphurId = (f.AmphurId ?? "").Trim(); f.NameTh = (f.NameTh ?? "").Trim(); f.NameEn = Norm(f.NameEn);
        string? err = null;
        if (f.AmphurId.Length != 4 || !f.AmphurId.All(char.IsDigit)) err = "รหัสอำเภอต้องเป็นตัวเลข 4 หลัก (เช่น 4701)";
        else if (f.NameTh.Length == 0) err = "กรุณากรอกชื่ออำเภอ";
        else if (f.NameTh.Length > 100 || (f.NameEn?.Length ?? 0) > 100) err = "ชื่อต้องไม่เกิน 100 ตัวอักษร";
        var province = f.AmphurId.Length >= 2 ? f.AmphurId[..2] : "";
        if (err == null && await c.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM m_province WHERE province_id=@province", new { province }) == 0)
            err = $"ไม่พบจังหวัดรหัส {province} ในตาราง m_province";
        if (err == null)
        {
            try
            {
                if (f.IsNew)
                {
                    var old = await c.ExecuteScalarAsync<int?>("SELECT is_active FROM m_amphur WHERE amphur_id=@AmphurId", f);
                    if (old == 1) throw new DupException();
                    if (old == 0)
                        await c.ExecuteAsync("UPDATE m_amphur SET name_th=@NameTh, name_en=@NameEn, is_active=1 WHERE amphur_id=@AmphurId", f);
                    else
                        await c.ExecuteAsync("INSERT INTO m_amphur(amphur_id,province_id,name_th,name_en,is_active) VALUES(@AmphurId,@province,@NameTh,@NameEn,1)",
                            new { f.AmphurId, province, f.NameTh, f.NameEn });
                }
                else
                    await c.ExecuteAsync("UPDATE m_amphur SET name_th=@NameTh, name_en=@NameEn WHERE amphur_id=@AmphurId", f);
                await Log(c, f.IsNew ? "CREATE_AMPHUR" : "UPDATE_AMPHUR", f.AmphurId);
                Flash("บันทึกข้อมูลอำเภอเรียบร้อยแล้ว");
                return RedirectToAction("Index", new { tab = "amphur" });
            }
            catch (DupException) { err = "รหัสอำเภอนี้มีอยู่แล้ว"; }
            catch (MySqlException ex)
            {
                err = ex.ErrorCode == MySqlErrorCode.DuplicateKeyEntry ? "รหัสอำเภอนี้มีอยู่แล้ว" : "บันทึกไม่ได้: " + ex.Message;
            }
        }
        return View(new AmphurFormVm { Form = f, Error = err });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> AmphurDelete(string id)
    {
        using var c = _db.Open();
        if (await c.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM m_tumbol WHERE amphur_id=@id AND is_active=1", new { id }) > 0)
        { FlashErr("ลบไม่ได้ เพราะอำเภอนี้ยังมีตำบลอยู่ กรุณาลบตำบลก่อน"); return RedirectToAction("Index", new { tab = "amphur" }); }
        await c.ExecuteAsync("UPDATE m_amphur SET is_active=0 WHERE amphur_id=@id", new { id });
        await Log(c, "DELETE_AMPHUR", id);
        Flash("ลบอำเภอเรียบร้อยแล้ว");
        return RedirectToAction("Index", new { tab = "amphur" });
    }
}
