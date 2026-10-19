using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SLDC.Web.Data;
using SLDC.Web.Models;

namespace SLDC.Web.Controllers;

// ประวัติการใช้งานระบบ (web_system_logs) สำหรับผู้ดูแลระบบ: ดู / ค้นหา / ล้างรายการเก่า
[Authorize(Roles = "admin")]
public class LogsController : AppController
{
    private const int PageSize = 25;
    public LogsController(Db db) : base(db) { }

    public async Task<IActionResult> Index(string? action = null, string? q = null, int page = 1)
    {
        using var c = _db.Open();
        action = Norm(action); q = Norm(q);
        var p = new DynamicParameters();
        var w = new List<string> { "1=1" };
        if (action != null) { w.Add("action_type=@action"); p.Add("action", action); }
        if (q != null) { w.Add("(username LIKE @q OR details LIKE @q OR target_id LIKE @q)"); p.Add("q", "%" + q + "%"); }
        var where = string.Join(" AND ", w);
        var total = await c.ExecuteScalarAsync<long>($"SELECT COUNT(*) FROM web_system_logs WHERE {where}", p);
        var (pg, pages) = PageInfo(total, page, PageSize);
        p.Add("size", PageSize); p.Add("off", (pg - 1) * PageSize);
        return View(new LogsVm
        {
            Action = action, Q = q,
            Actions = (await c.QueryAsync<string>("SELECT DISTINCT action_type FROM web_system_logs ORDER BY action_type")).ToList(),
            Rows = new Paged<LogRow>
            {
                Page = pg, Pages = pages, Total = total,
                Items = (await c.QueryAsync<LogRow>($@"SELECT log_id, username, action_type, target_id, details, ip_address, created_at
                    FROM web_system_logs WHERE {where} ORDER BY log_id DESC LIMIT @size OFFSET @off", p)).ToList()
            }
        });
    }

    // ล้างบันทึกที่เก่ากว่าจำนวนวันที่กำหนด (อย่างน้อย 30 วัน)
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Purge(int days)
    {
        if (days < 30) { FlashErr("ล้างได้เฉพาะรายการที่เก่ากว่า 30 วันขึ้นไป"); return RedirectToAction("Index"); }
        using var c = _db.Open();
        var n = await c.ExecuteAsync("DELETE FROM web_system_logs WHERE created_at < DATE_SUB(NOW(), INTERVAL @days DAY)", new { days });
        await Log(c, "PURGE_LOGS", null, $"ลบ {n} รายการที่เก่ากว่า {days} วัน");
        Flash($"ล้างบันทึกเก่าแล้ว {n:N0} รายการ");
        return RedirectToAction("Index");
    }
}
