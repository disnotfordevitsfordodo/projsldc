using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MySqlConnector;
using SLDC.Web.Data;
using SLDC.Web.Models;

namespace SLDC.Web.Controllers;

[Authorize(Roles = "admin")]
public class UsersController : AppController
{
    public UsersController(Db db) : base(db) { }

    public async Task<IActionResult> Index(string? q = null, int page = 1)
    {
        using var c = _db.Open();
        q = Norm(q);
        var p = new DynamicParameters();
        var where = "1=1";
        if (q != null) { where = "(u.username LIKE @q OR u.full_name LIKE @q)"; p.Add("q", "%" + q + "%"); }
        var total = await c.ExecuteScalarAsync<long>($"SELECT COUNT(*) FROM web_users u WHERE {where}", p);
        var (pg, pages) = PageInfo(total, page, 10);
        p.Add("size", 10); p.Add("off", (pg - 1) * 10);
        var items = (await c.QueryAsync<UserRow>($@"SELECT u.user_id, u.username, u.full_name, u.role, u.amphur_id, u.tumbol_id, u.is_active,
                a.name_th AS amphur_name, t.name_th AS tumbol_name
            FROM web_users u
            LEFT JOIN m_amphur a ON a.amphur_id = u.amphur_id
            LEFT JOIN m_tumbol t ON t.tumbol_id = u.tumbol_id
            WHERE {where} ORDER BY u.user_id LIMIT @size OFFSET @off", p)).ToList();
        return View(new UsersVm { Q = q, Users = new Paged<UserRow> { Items = items, Page = pg, Pages = pages, Total = total } });
    }

    private async Task<List<Opt>> AllTumbols(System.Data.IDbConnection c) =>
        (await c.QueryAsync<Opt>(@"SELECT t.tumbol_id AS Id, CONCAT('ต.', t.name_th, ' (อ.', a.name_th, ')') AS Name
            FROM m_tumbol t JOIN m_amphur a ON a.amphur_id = t.amphur_id WHERE t.is_active=1 ORDER BY t.tumbol_id")).ToList();

    [HttpGet]
    public async Task<IActionResult> Edit(int? id)
    {
        using var c = _db.Open();
        var f = new UserForm();
        if (id != null)
        {
            var u = await c.QueryFirstOrDefaultAsync<UserRow>(
                "SELECT user_id, username, full_name, role, amphur_id, tumbol_id, is_active FROM web_users WHERE user_id=@id", new { id });
            if (u == null) return NotFound();
            f = new UserForm { UserId = u.UserId, Username = u.Username, FullName = u.FullName, Role = u.Role, AmphurId = u.AmphurId, TumbolId = u.TumbolId, IsActive = u.IsActive };
        }
        return View(new UserFormVm { Form = f, Amphurs = await Amphurs(c), Tumbols = await AllTumbols(c) });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(UserForm f)
    {
        using var c = _db.Open();
        f.Username = (f.Username ?? "").Trim();
        f.AmphurId = Norm(f.AmphurId); f.TumbolId = Norm(f.TumbolId);
        if (f.Role == "admin") { f.AmphurId = null; f.TumbolId = null; }
        if (f.Role == "amphur") f.TumbolId = null;

        string? err = null;
        if (f.Username.Length < 3) err = "ชื่อผู้ใช้ต้องยาวอย่างน้อย 3 ตัวอักษร";
        else if (f.UserId == 0 && (f.Password ?? "").Length < 8) err = "รหัสผ่านต้องมีอย่างน้อย 8 ตัวอักษร";
        else if (!string.IsNullOrEmpty(f.Password) && f.Password.Length < 8) err = "รหัสผ่านใหม่ต้องมีอย่างน้อย 8 ตัวอักษร";
        else if (f.Role == "amphur" && f.AmphurId == null) err = "ผู้ใช้ระดับอำเภอต้องเลือกอำเภอ";
        else if (f.Role == "staff" && f.TumbolId == null) err = "เจ้าหน้าที่ตำบลต้องเลือกตำบล";

        if (err == null && f.UserId != 0)
        {
            // ต้องเหลือผู้ดูแลระบบที่ใช้งานได้อย่างน้อย 1 คนเสมอ
            var cur = await c.QueryFirstOrDefaultAsync<UserRow>("SELECT user_id, role, is_active FROM web_users WHERE user_id=@id", new { id = f.UserId });
            if (cur == null) return NotFound();
            if (cur.Role == "admin" && cur.IsActive && (f.Role != "admin" || !f.IsActive) &&
                await c.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM web_users WHERE role='admin' AND is_active=1") <= 1)
                err = "ไม่สามารถเปลี่ยนสิทธิ์หรือปิดใช้งานผู้ดูแลระบบคนสุดท้ายได้";
        }
        if (err == null)
        {
            try
            {
                if (f.UserId == 0)
                    await c.ExecuteAsync(@"INSERT INTO web_users(username,password_hash,full_name,role,amphur_id,tumbol_id,is_active)
                        VALUES(@Username,@hash,@FullName,@Role,@AmphurId,@TumbolId,@IsActive)",
                        new { f.Username, hash = BCrypt.Net.BCrypt.HashPassword(f.Password!), f.FullName, f.Role, f.AmphurId, f.TumbolId, f.IsActive });
                else
                {
                    await c.ExecuteAsync(@"UPDATE web_users SET username=@Username, full_name=@FullName, role=@Role,
                        amphur_id=@AmphurId, tumbol_id=@TumbolId, is_active=@IsActive WHERE user_id=@UserId", f);
                    if (!string.IsNullOrEmpty(f.Password))
                        await c.ExecuteAsync("UPDATE web_users SET password_hash=@hash WHERE user_id=@UserId",
                            new { hash = BCrypt.Net.BCrypt.HashPassword(f.Password), f.UserId });
                }
                await Log(c, f.UserId == 0 ? "CREATE_USER" : "UPDATE_USER", f.Username);
                TempData["Msg"] = "บันทึกข้อมูลผู้ใช้เรียบร้อยแล้ว";
                return RedirectToAction("Index");
            }
            catch (MySqlException ex)
            {
                err = ex.ErrorCode == MySqlErrorCode.DuplicateKeyEntry ? "ชื่อผู้ใช้นี้ถูกใช้แล้ว" : "บันทึกไม่ได้: " + ex.Message;
            }
        }
        return View(new UserFormVm { Form = f, Error = err, Amphurs = await Amphurs(c), Tumbols = await AllTumbols(c) });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        if (id == UserId) { TempData["Msg"] = "ไม่สามารถลบผู้ใช้ที่กำลังเข้าสู่ระบบอยู่ได้"; return RedirectToAction("Index"); }
        using var c = _db.Open();
        var target = await c.QueryFirstOrDefaultAsync<UserRow>("SELECT user_id, username, role, is_active FROM web_users WHERE user_id=@id", new { id });
        if (target == null) return NotFound();
        if (target.Role == "admin" && target.IsActive &&
            await c.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM web_users WHERE role='admin' AND is_active=1") <= 1)
        { TempData["Msg"] = "ไม่สามารถลบผู้ดูแลระบบคนสุดท้ายได้"; return RedirectToAction("Index"); }
        try
        {
            await c.ExecuteAsync("DELETE FROM web_users WHERE user_id=@id", new { id });
            await Log(c, "DELETE_USER", id.ToString());
            TempData["Msg"] = $"ลบผู้ใช้ {target.Username} เรียบร้อยแล้ว";
        }
        catch (MySqlException ex) when (ex.ErrorCode == MySqlErrorCode.RowIsReferenced || ex.ErrorCode == MySqlErrorCode.RowIsReferenced2)
        {
            // ผู้ใช้นี้มีประวัติการใช้งาน/ข้อมูลที่อ้างอิงอยู่ ลบจริงไม่ได้ จึงปิดการใช้งานแทน
            await c.ExecuteAsync("UPDATE web_users SET is_active=0 WHERE user_id=@id", new { id });
            await Log(c, "DEACTIVATE_USER", id.ToString());
            TempData["Msg"] = $"ผู้ใช้ {target.Username} มีประวัติการใช้งานอ้างอิงอยู่ จึงปิดการใช้งานแทนการลบ";
        }
        return RedirectToAction("Index");
    }
}
