using System.Security.Claims;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using SLDC.Web.Data;
using SLDC.Web.Models;

namespace SLDC.Web.Controllers;

[Authorize]
public class WelfareController : Controller
{
    private const int PageSize = 10;
    private const int CmdTimeout = 120;   // วินาที (ค่าเริ่มต้นของ Dapper/MySqlConnector คือ 30)
    private readonly Db _db;
    private readonly IMemoryCache _cache;
    public WelfareController(Db db, IMemoryCache cache) { _db = db; _cache = cache; }

    private static string? Norm(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    public async Task<IActionResult> Index(int year = 0, string? amphurId = null, string? tumbolId = null,
                                           int? group = null, string? q = null, int page = 1)
    {
        using var c = _db.Open();
        year = await _db.YearAsync(c, year);
        amphurId = Norm(amphurId); tumbolId = Norm(tumbolId); q = Norm(q);

        // จำกัดสิทธิ์ตาม role (amphur เห็นเฉพาะอำเภอตัวเอง, staff เห็นเฉพาะตำบลตัวเอง)
        var role = User.FindFirst(ClaimTypes.Role)?.Value;
        if (role == "amphur") amphurId = User.FindFirst("amphur_id")?.Value;
        if (role == "staff") { tumbolId = User.FindFirst("tumbol_id")?.Value; amphurId = null; }

        var hasArea = amphurId != null || tumbolId != null;

        var p = new DynamicParameters();
        p.Add("year", year);
        var w = new List<string> { "survey_year=@year" };
        if (amphurId != null) { w.Add("amphur_id=@amphurId"); p.Add("amphurId", amphurId); }
        if (tumbolId != null) { w.Add("tumbol_id=@tumbolId"); p.Add("tumbolId", tumbolId); }
        var whereSummary = string.Join(" AND ", w);

        if (group != null) { w.Add("welfare_status_id=@group"); p.Add("group", group); }
        if (q != null) { w.Add("(full_name LIKE @q OR house_code LIKE @q)"); p.Add("q", "%" + q + "%"); }
        var whereList = string.Join(" AND ", w);

        var vm = new WelfareVm { Year = year, AmphurId = amphurId, TumbolId = tumbolId, Group = group, Q = q };

        // 1) สรุปรายตำบล: แคช 10 นาที (ตัวเลขสรุปไม่จำเป็นต้องสดทุกวินาที)
        var key = $"welfare-summary:{year}:{amphurId}:{tumbolId}";
        vm.Summary = await _cache.GetOrCreateAsync(key, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10);
            return (await c.QueryAsync<WelfareRow>(
                $"SELECT * FROM v_welfare_by_tumbol WHERE {whereSummary} ORDER BY tumbol_id", p,
                commandTimeout: CmdTimeout)).ToList();
        }) ?? new List<WelfareRow>();

        // 2) จำนวนรายการ: ถ้าไม่ได้ค้นหาชื่อ ใช้ผลรวมจากตารางสรุป ไม่ต้อง COUNT(*) ทั้งตารางสมาชิก
        if (q == null)
        {
            vm.RowCount = group switch
            {
                null => vm.SumAll,
                0 => vm.SumWait,
                1 => vm.SumG1,
                2 => vm.SumG2,
                3 => vm.SumG3,
                _ => 0
            };
        }
        else if (hasArea)
        {
            vm.RowCount = await c.ExecuteScalarAsync<long>(
                $"SELECT COUNT(*) FROM v_member_welfare_list WHERE {whereList}", p, commandTimeout: CmdTimeout);
        }

        vm.Pages = (int)Math.Max(1, (vm.RowCount + PageSize - 1) / PageSize);
        vm.Page = Math.Clamp(page, 1, vm.Pages);

        // 3) รายชื่อบุคคล: ดึงเมื่อเลือกอำเภอหรือตำบลแล้วเท่านั้น (ไม่ไล่ทั้งจังหวัด)
        if (hasArea)
        {
            p.Add("size", PageSize);
            p.Add("off", (vm.Page - 1) * PageSize);
            // ไม่ได้ค้นชื่อ: เลือก member_id ของหน้านั้นด้วยคิวรีแคบ ๆ ก่อน แล้วค่อยดึงรายละเอียดจาก view
            var extra = (amphurId != null ? " AND t.amphur_id=@amphurId" : "")
                      + (tumbolId != null ? " AND t.tumbol_id=@tumbolId" : "")
                      + (group != null ? " AND COALESCE(w.welfare_status_id, 0)=@group" : "");
            vm.Members = (await c.QueryAsync<MemberRow>(
                q == null
                    ? MemberQueries.Page("v_member_welfare_list", extra)
                    : $"SELECT * FROM v_member_welfare_list WHERE {whereList} ORDER BY house_code, member_id LIMIT @size OFFSET @off",
                p, commandTimeout: CmdTimeout)).ToList();
        }

        vm.Amphurs = (await c.QueryAsync<Opt>(
            "SELECT amphur_id AS Id, name_th AS Name FROM m_amphur WHERE is_active=1 ORDER BY amphur_id")).ToList();
        if (amphurId != null)
            vm.Tumbols = (await c.QueryAsync<Opt>(
                "SELECT tumbol_id AS Id, name_th AS Name FROM m_tumbol WHERE amphur_id=@amphurId AND is_active=1 ORDER BY tumbol_id",
                new { amphurId })).ToList();

        return View(vm);
    }
}
